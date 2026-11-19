#!/usr/bin/env python3
"""Gives the wixl-built Valco Weave MSI the same setup wizard as the rest of the
Valco suite (Valco Express, Valco Code): WiX's standard "InstallDir" wizard with
the Vista aurora banner and side artwork, the licence page, folder choice, and a
"Launch Valco Weave" box on the last page.

wixl (msitools) can't build WixUI itself, so the wizard's tables were exported
once from a WiX-built Valco installer into ./wixui and are imported here with
msibuild. Only the artwork, licence text and a few properties are swapped.

usage: msi_wixui.py <file.msi>     (run from anywhere; needs msitools)
       FILES_DIR=<dir with the exe/dll>  (defaults to the MSI's folder)
"""
import os
import shutil
import struct
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
TPL = os.path.join(HERE, 'wixui')
ASSETS = os.path.join(HERE, '..', 'assets')
MSI = os.path.abspath(sys.argv[1])
APP = 'Valco Weave'
EXE_FILE_ID = 'ValcoWeaveExe'
INSTALLDIR = 'INSTALLDIR'


def export(table):
    out = subprocess.run(['msiinfo', 'export', MSI, table], capture_output=True, text=True, check=True).stdout
    lines = out.replace('\r\n', '\n').strip('\n').split('\n')
    return [l.split('\t') for l in lines[3:]] if len(lines) > 3 else []


def write_idt(folder, table, cols, types, keys, rows):
    with open(os.path.join(folder, table + '.idt'), 'w', encoding='cp1252', newline='') as f:
        f.write('\t'.join(cols) + '\r\n')
        f.write('\t'.join(types) + '\r\n')
        f.write('\t'.join([table] + keys) + '\r\n')
        for r in rows:
            f.write('\t'.join('' if v is None else str(v) for v in r) + '\r\n')


def read_tpl_rows(name):
    data = open(os.path.join(TPL, name + '.idt'), 'rb').read().decode('cp1252')
    head, _, body = data.partition('\r\n')
    t2, _, body = body.partition('\r\n')
    t3, _, body = body.partition('\r\n')
    rows = [r.split('\t') for r in body.split('\r\n') if r]
    return [head.split('\t'), t2.split('\t'), t3.split('\t')], rows


tmp = tempfile.mkdtemp(prefix='valco-wixui-')
os.makedirs(os.path.join(tmp, 'Binary'))

# --- wizard tables, copied as they are
for t in ['Dialog', 'ControlEvent', 'ControlCondition', 'EventMapping', 'TextStyle', 'RadioButton',
          'UIText', 'CheckBox', 'ListBox', 'InstallUISequence', 'AdminUISequence']:
    shutil.copy(os.path.join(TPL, t + '.idt'), tmp)

# --- Control: our licence text (RTF; raw newlines can't go in an IDT row, and RTF doesn't need them)
hdr, rows = read_tpl_rows('Control')
rtf = open(os.path.join(HERE, 'License.rtf'), encoding='cp1252').read().replace('\r', '').replace('\n', ' ')
fixed = []
for r in rows:
    if r[0] == 'LicenseAgreementDlg' and r[1] == 'LicenseText':
        r = r[:9] + [rtf] + r[10:]
    fixed.append([c.replace('\n', ' ') for c in r])
write_idt(tmp, 'Control', hdr[0], hdr[1], hdr[2][1:], fixed)

# --- Binary: WiX's helper DLLs and little icons, plus the Valco Weave banner + side art
bin_rows = []
for name in ['Wix4UtilCA_X64', 'WixUiCa_X64', 'WixUI_Bmp_Up', 'WixUI_Bmp_New', 'WixUI_Ico_Exclam', 'WixUI_Ico_Info']:
    shutil.copy(os.path.join(TPL, 'Binary', name), os.path.join(tmp, 'Binary', name))
    bin_rows.append([name, name])
for name, src in [('WixUI_Bmp_Banner', 'banner.bmp'), ('WixUI_Bmp_Dialog', 'dialog.bmp')]:
    shutil.copy(os.path.join(ASSETS, src), os.path.join(tmp, 'Binary', name))
    bin_rows.append([name, name])
write_idt(tmp, 'Binary', ['Name', 'Data'], ['s72', 'v0'], ['Name'], bin_rows)

# --- custom actions: WiX's path check / licence print, and "launch after install"
ca = [r for r in export('CustomAction') if r[0] not in ('LaunchApplication', 'WixUIPrintEula_X64', 'WixUIValidatePath_X64')]
ca += [['LaunchApplication', '1', 'Wix4UtilCA_X64', 'WixShellExec', ''],
       ['WixUIPrintEula_X64', '65', 'WixUiCa_X64', 'PrintEula', ''],
       ['WixUIValidatePath_X64', '65', 'WixUiCa_X64', 'ValidatePath', '']]
write_idt(tmp, 'CustomAction', ['Action', 'Type', 'Source', 'Target', 'ExtendedType'], ['s72', 'i2', 'S72', 'S255', 'I4'], ['Action'], ca)

# --- properties the wizard reads
props = {r[0]: r[1] for r in export('Property')}
props.update({
    'DefaultUIFont': 'WixUI_Font_Normal',
    'ErrorDialog': 'ErrorDlg',
    'WixUIRMOption': 'UseRM',
    'WIXUI_INSTALLDIR': INSTALLDIR,
    'WIXUI_EXITDIALOGOPTIONALCHECKBOXTEXT': f'Launch {APP}',
    'WIXUI_EXITDIALOGOPTIONALCHECKBOX': '1',
    'WixShellExecTarget': f'[#{EXE_FILE_ID}]',
    'ARPNOMODIFY': '1',
    'REINSTALLMODE': 'amus',   # always replace every file on upgrade
})
sec = [s for s in props.get('SecureCustomProperties', '').split(';') if s]
for s in [INSTALLDIR, 'WIX_UPGRADE_DETECTED', 'WIX_DOWNGRADE_DETECTED']:
    if s not in sec:
        sec.append(s)
props['SecureCustomProperties'] = ';'.join(sec)
write_idt(tmp, 'Property', ['Property', 'Value'], ['s72', 'l0'], ['Property'], sorted(props.items()))


# --- File versions: wixl leaves File.Version empty, so Windows treats the exe/dll as
# unversioned and, on upgrade, can keep the old files and then delete them. Fill them in.
def pe_version(path):
    try:
        data = open(path, 'rb').read()
    except OSError:
        return None
    i = data.find(struct.pack('<I', 0xFEEF04BD))
    if i < 0:
        return None
    ms, ls = struct.unpack_from('<II', data, i + 8)
    return f'{ms >> 16}.{ms & 0xFFFF}.{ls >> 16}.{ls & 0xFFFF}'


files_dir = os.environ.get('FILES_DIR') or os.path.dirname(MSI)
file_rows = export('File')
for r in file_rows:
    while len(r) < 8:
        r.append('')
    if not r[4]:
        v = pe_version(os.path.join(files_dir, r[2].split('|')[-1]))
        if v:
            r[4], r[5] = v, r[5] or '1033'
write_idt(tmp, 'File', ['File', 'Component_', 'FileName', 'FileSize', 'Version', 'Language', 'Attributes', 'Sequence'],
          ['s72', 's72', 'l255', 'i4', 'S72', 'S20', 'I2', 'i2'], ['File'], file_rows)

cwd = os.getcwd()
os.chdir(tmp)
for t in ['File', 'Binary', 'TextStyle', 'UIText', 'Dialog', 'Control', 'ControlEvent', 'EventMapping', 'ControlCondition',
          'RadioButton', 'CheckBox', 'ListBox', 'CustomAction', 'Property', 'InstallUISequence', 'AdminUISequence']:
    subprocess.run(['msibuild', MSI, '-i', t + '.idt'], check=True)
os.chdir(cwd)
shutil.rmtree(tmp)
print('Valco setup wizard added to', MSI)
