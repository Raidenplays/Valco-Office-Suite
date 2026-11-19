# Generates src/ValcoSpreadsheets/Themes/SheetIcons.xaml: the Excel-specific ribbon icons
# (vector DrawingImages on a 24x24 grid, same Vista style and brushes as the suite icons).
import os
HERE = os.path.dirname(os.path.abspath(__file__))
exec(open(os.path.join(HERE, '_icon_helpers.py')).read())
exec(open(os.path.join(HERE, '_extra_brushes.py')).read())

def grid(x=2.5, y=3.5, w=19, h=17, rows=3, cols=3, head=True):
    d = ''
    for i in range(1, rows):
        yy = y + h * i / rows
        d += f'M{x},{yy:.2f} H{x+w} '
    for i in range(1, cols):
        xx = x + w * i / cols
        d += f'M{xx:.2f},{y} V{y+h} '
    out = R('#FFFFFFFF', 'P_Tbl', x, y, w, h)
    if head:
        out += R('B_TblHead', None, x + 0.5, y + 0.5, w - 1, h / rows - 0.5)
    return out + P(None, 'P_Tbl', d.strip())

def arrow_down(x, y, l=7, w=3):
    return P('B_Arrow', 'P_Arrow', f'M{x-w/2},{y} H{x+w/2} V{y+l} H{x+w*1.4} L{x},{y+l+w*1.8} L{x-w*1.4},{y+l} H{x-w/2} Z')

SIGMA = 'M4,3 H19 V7.5 H17.4 L16.6,5.6 H8.6 L13.6,12 L8.4,18.4 H17 L17.8,16.4 H19.4 L19,21 H4 V19.8 L10.4,12 L4,4.2 Z'
G('IconAutoSum', P('#FF2B5797', None, SIGMA))
G('IconFillDown', grid(3, 2.5, 13, 19, 3, 1, False), R('B_TblHead', None, 3.5, 3, 12, 5.5), arrow_down(19.5, 6, 8, 3))
G('IconClear', P('B_Eraser', 'P_Red', 'M13,3 L21.5,11.5 L12.5,20.5 H7.5 L3,16 Z'), P('#FFFFFFFF', 'P_Red', 'M3,16 L8.5,10.5 L15,17 L12.5,20.5 H7.5 Z'), P(None, 'P_Grey', 'M12,21.5 H21.5'))
G('IconSortAZ', P(None, 'P_Num', 'M2.5,10 L5.2,3 L7.9,10 M3.4,7.6 H7 M2.6,13.5 H8 L2.6,20.5 H8'), P('B_Arrow', 'P_Arrow', 'M14.5,3 H17.5 V16 H20.5 L16,21.5 L11.5,16 H14.5 Z'))
G('IconSortZA', P(None, 'P_Num', 'M2.6,3 H8 L2.6,10 H8 M2.5,20.5 L5.2,13.5 L7.9,20.5 M3.4,18.1 H7'), P('B_Arrow', 'P_Arrow', 'M14.5,3 H17.5 V16 H20.5 L16,21.5 L11.5,16 H14.5 Z'))
G('IconFilter', P('B_Arrow', 'P_Arrow', 'M2.5,3.5 H21.5 L14,12.5 V20.5 L10,18.5 V12.5 Z'), P(None, 'P_WhiteFat', 'M6,5.5 H12'))
G('IconClearFilter', P('B_Grey', 'P_Grey', 'M2.5,3.5 H17.5 L11.5,11 V18 L8.5,16.5 V11 Z'), P(None, 'P_RedThick', 'M15,14 L21,20 M21,14 L15,20'))
G('IconReapply', P('B_Arrow', 'P_Arrow', 'M2.5,3.5 H17.5 L11.5,11 V18 L8.5,16.5 V11 Z'), P('B_Green', 'P_Green', 'M18,12 C21,12 22.5,14 22.5,16.5 L23.8,16.5 L21.5,20 L19.2,16.5 L20.5,16.5 C20.5,15 19.5,14 18,14 Z'))
G('IconFunction', P('#FF2B5797', None, 'M9.6,3 C11.2,2.6 12.6,2.8 13.4,3.6 L12.6,5 C12,4.5 11.2,4.4 10.6,4.8 C10,5.2 9.8,6 9.6,7.4 H12.4 L12.1,8.9 H9.3 L7.8,17.6 C7.4,20.2 5.6,21.6 3,21 L3.4,19.4 C4.8,19.8 5.6,19.2 5.9,17.6 L7.4,8.9 H5.6 L5.9,7.4 H7.7 C8,5 8.4,3.5 9.6,3 Z'),
  P('#FF2B5797', None, 'M13.2,10 H15.6 L17.2,12.6 L19.4,10 H21.6 L18.2,14 L20.6,18 H18.2 L16.6,15.4 L14.4,18 H12.2 L15.6,14 Z'))
G('IconInsertCells', grid(2.5, 3.5, 15, 15, 3, 3, False), R('B_Gold', 'P_Gold', 7.5, 8.5, 5, 5), P(None, 'P_Plus', 'M19.5,13 V22 M15,17.5 H24'))
G('IconDeleteCells', grid(2.5, 3.5, 15, 15, 3, 3, False), R('B_Gold', 'P_Gold', 7.5, 8.5, 5, 5), P(None, 'P_RedThick', 'M16.5,14.5 L22.5,20.5 M22.5,14.5 L16.5,20.5'))
G('IconFormatCells', grid(2.5, 3.5, 15, 15, 3, 3, False), R('B_Gold', None, 7.5, 8.5, 5, 5), P('B_Gold', 'P_Gold', 'M21.6,9.2 L23.6,11.2 L15,19.8 L12.2,20.6 L13,17.8 Z'))
G('IconInsertRows', grid(2.5, 2.5, 19, 19, 4, 3, False), R('B_Gold', None, 3, 12, 18, 4.3), P(None, 'P_Plus', 'M5,14.2 H9'))
G('IconInsertCols', grid(2.5, 2.5, 19, 19, 3, 4, False), R('B_Gold', None, 12, 3, 4.3, 18), P(None, 'P_Plus', 'M14.2,5 V9'))
G('IconDeleteRows', grid(2.5, 2.5, 19, 19, 4, 3, False), R('#FFFFD0CC', None, 3, 12, 18, 4.3), P(None, 'P_RedThick', 'M8,12.6 L11,15.6 M11,12.6 L8,15.6'))
G('IconDeleteCols', grid(2.5, 2.5, 19, 19, 3, 4, False), R('#FFFFD0CC', None, 12, 3, 4.3, 18), P(None, 'P_RedThick', 'M12.6,8 L15.6,11 M15.6,8 L12.6,11'))
G('IconRowHeight', P(None, 'P_Tbl', 'M2,6 H22 M2,18 H22'), P('B_Arrow', 'P_Arrow', 'M12,6.5 L15,10 H13.2 V14 H15 L12,17.5 L9,14 H10.8 V10 H9 Z'))
G('IconColWidth', P(None, 'P_Tbl', 'M6,2 V22 M18,2 V22'), P('B_Arrow', 'P_Arrow', 'M6.5,12 L10,9 V10.8 H14 V9 L17.5,12 L14,15 V13.2 H10 V15 Z'))
G('IconWrapText', P(None, 'P_Bar', 'M3,5 H21 M3,12 H17 C21,12 21,19 17,19 H12 M3,19 H8'), P('B_Arrow', 'P_Arrow', 'M14.5,16 L11,19 L14.5,22 Z'))
G('IconMergeCenter', R('#FFFFFFFF', 'P_Tbl', 2, 4, 20, 16), P(None, 'P_Dash2', 'M12,4 V8 M12,16 V20'), P('B_Arrow', 'P_Arrow', 'M3.5,12 L7.5,9 V10.8 H16.5 V9 L20.5,12 L16.5,15 V13.2 H7.5 V15 Z'))
G('IconCurrency', P(None, 'P_Num', 'M17,6.5 C16,4.5 14,4 12,4 C8.5,4 7,6 7,8 C7,12.5 17,11 17,15.8 C17,18.4 15,20 12,20 C9.5,20 7.8,19 7,17 M12,2 V22'))
G('IconPercent', P(None, 'P_Num', 'M18.5,4 L5.5,20'), E('#FFFFFFFF', 'P_Num', 7, 7, 2.6, 3), E('#FFFFFFFF', 'P_Num', 17, 17, 2.6, 3))
G('IconComma', P('#FF2B5797', None, 'M5,21 C7.5,20 8.5,18.5 8.5,16.5 H6 V13 H10 V16.5 C10,19.5 8.5,21.5 5.6,22.4 Z'), P(None, 'P_Num', 'M13,7 L15,5 V13 M17,7.2 C17.4,5.4 21,5.2 21,7.2 C21,9 17,10.6 17,13 H21.2'))
G('IconIncDecimal', P('#FF2B5797', None, 'M2.5,19 H4.5 V21 H2.5 Z'), P(None, 'P_Num', 'M7,14 L8.5,13 V21 M12,14 L13.5,13 V21 M17,14 L18.5,13 V21'), P('B_Green', 'P_Green', 'M3,7 L8,3 V5.5 H18 V8.5 H8 V11 Z'))
G('IconDecDecimal', P('#FF2B5797', None, 'M2.5,19 H4.5 V21 H2.5 Z'), P(None, 'P_Num', 'M7,14 L8.5,13 V21 M12,14 L13.5,13 V21'), P('B_Red', 'P_Red', 'M21,7 L16,3 V5.5 H6 V8.5 H16 V11 Z'))
G('IconFillColor', P('B_Grey', 'P_Grey', 'M6,9.5 L12,3.5 L18.5,10 L12.5,16 Z'), P(None, 'P_Grey', 'M9,3 L12,6'), P('B_Gold', 'P_Gold', 'M19.5,11.5 C21.5,14.5 22,16.5 20.3,17.5 C18.7,18.3 17.5,16.5 19.5,11.5 Z'))
G('IconFreeze', grid(2.5, 2.5, 19, 19, 3, 3, False), R('#FFD7E7FA', None, 3, 3, 18, 6), R('#FFD7E7FA', None, 3, 9, 6, 12), P(None, 'P_Rule', 'M2.5,8.8 H21.5 M8.8,2.5 V21.5'))
G('IconCondFormat', grid(2.5, 3, 19, 18, 3, 2, False), R('#FFF8696B', None, 3, 3.5, 9, 5.5), R('#FFFFEB84', None, 3, 9.5, 9, 5.5), R('#FF63BE7B', None, 3, 15.5, 9, 5), R('B_Arrow', None, 13, 5, 6, 2.5), R('B_Arrow', None, 13, 11, 4, 2.5), R('B_Arrow', None, 13, 17, 7.5, 2.5))
G('IconFormatTable', grid(2.5, 3.5, 19, 17, 4, 3, True), R('#FFDCE6F2', None, 3, 12, 18, 4))
G('IconCellStyles', R('#FFFFFFFF', 'P_Tbl', 2.5, 4, 9, 7), R('#FFC6EFCE', 'P_Tbl', 12.5, 4, 9, 7), R('#FFFFC7CE', 'P_Tbl', 2.5, 13, 9, 7), R('#FFFFEB9C', 'P_Tbl', 12.5, 13, 9, 7))
G('IconNewSheet', grid(2, 5, 16, 16, 3, 3, True), P('B_Gold', 'P_Gold', 'M18,0.8 L19.5,4.2 L23.2,4.5 L20.4,6.9 L21.2,10.5 L18,8.6 L14.8,10.5 L15.6,6.9 L12.8,4.5 L16.5,4.2 Z'))
G('IconSheet', grid(2.5, 3.5, 19, 17, 4, 3, True))
G('IconNameManager', R('#FFFFFFFF', 'P_Tbl', 2, 5, 20, 14, 1.5), P(None, 'P_Num', 'M5,15 V9 L9,15 V9 M12,9 H19 M12,12 H19 M12,15 H17'))
G('IconCalcNow', R('B_Grey', 'P_Grey', 4, 2.5, 16, 19, 1.5), R('#FFE8F3E0', 'P_GreyThin', 6.5, 4.5, 11, 4), R('B_Arrow', None, 6.5, 11, 3, 2.5), R('B_Arrow', None, 10.5, 11, 3, 2.5), R('B_Arrow', None, 14.5, 11, 3, 2.5), R('B_Arrow', None, 6.5, 15, 3, 2.5), R('B_Arrow', None, 10.5, 15, 3, 2.5), R('B_Green', None, 14.5, 15, 3, 2.5))
G('IconTextToColumns', R('#FFFFFFFF', 'P_Tbl', 2, 4, 20, 16), P(None, 'P_Text', 'M4,8 H10 M14,8 H20 M4,12 H9 M14,12 H19 M4,16 H10 M14,16 H18'), P(None, 'P_Dash', 'M12,3 V21'))
G('IconRemoveDup', grid(2, 3, 13, 14, 3, 2, False), R('#FFFFD0CC', None, 2.5, 7.8, 12, 4.4), P(None, 'P_RedThick', 'M16,13 L22,19 M22,13 L16,19'))
G('IconValidation', grid(2.5, 3.5, 15, 15, 3, 2, False), P(None, 'P_Check', 'M13,16 L16.5,19.5 L22.5,11'))
G('IconGroup', P(None, 'P_Tbl', 'M3,4 V20 H5 M3,4 H5'), P(None, 'P_Bar', 'M8,6 H21 M8,10 H21 M8,14 H21 M8,18 H21'), P('B_Arrow', 'P_Arrow', 'M1,10 H5 V14 H1 Z'))
G('IconNormalView', grid(2.5, 3.5, 19, 17, 4, 4, False))
G('IconPageLayoutView', R('#FFFFFFFF', 'P_Grey', 3, 2, 18, 20), grid(5.5, 7, 13, 12, 3, 3, False), P(None, 'P_Text', 'M6,4.5 H12'))
G('IconPageBreakPreview', R('#FFFFFFFF', 'P_Grey', 2.5, 2.5, 19, 19), P(None, 'P_Dash', 'M12,2.5 V21.5 M2.5,12 H21.5'), P('#FF9AA8B8', None, 'M5,5 H9 V8 H5 Z M15,15 H19 V18 H15 Z'))
G('IconPrintArea', R('#FFFFFFFF', 'P_Grey', 2.5, 2.5, 19, 19), P(None, 'P_Dash', 'M5,6 H16 V16 H5 Z'), R('B_Grey', 'P_Grey', 13, 14, 9, 7, 1))
G('IconPrintTitles', R('#FFFFFFFF', 'P_Grey', 3, 2, 18, 20), R('B_TblHead', None, 3.5, 2.5, 17, 4), P(None, 'P_Text', 'M6,10 H18 M6,13 H18 M6,16 H18 M6,19 H14'))
G('IconHideRow', P(None, 'P_Tbl', 'M2,5 H22 M2,19 H22'), P(None, 'P_Rule', 'M2,11.5 H22 M2,12.5 H22'))
G('IconColumnChart', P(None, 'P_Grey', 'M2.5,2.5 V21.5 H21.5'), R('B_Arrow', 'P_Arrow', 5, 11, 4, 10), R('B_Gold', 'P_Gold', 10.5, 5, 4, 16), R('B_Green', 'P_Green', 16, 8, 4, 13))
G('IconBarChart', P(None, 'P_Grey', 'M2.5,2.5 V21.5 H21.5'), R('B_Arrow', 'P_Arrow', 3, 4, 10, 4), R('B_Gold', 'P_Gold', 3, 10, 17, 4), R('B_Green', 'P_Green', 3, 16, 13, 4))
G('IconLineChart', P(None, 'P_Grey', 'M2.5,2.5 V21.5 H21.5'), P(None, 'P_Num', 'M4,17 L9,10 L13,14 L20.5,4.5'), P(None, 'P_Gold', 'M4,20 L9,16 L14,18 L20.5,12'))
G('IconPieChart', P('B_Arrow', 'P_Arrow', 'M12,12 V2.5 A9.5,9.5 0 1 1 2.6,13.4 Z'), P('B_Gold', 'P_Gold', 'M11,11 L1.6,12.4 A9.5,9.5 0 0 1 11,1.5 Z'))
G('IconAreaChart', P(None, 'P_Grey', 'M2.5,2.5 V21.5 H21.5'), P('B_Arrow', 'P_Arrow', 'M3,21 V13 L8,8 L13,12 L20.5,5 V21 Z'), P('B_Green', 'P_Green', 'M3,21 V18 L9,14 L14,17 L20.5,13 V21 Z'))
G('IconScatterChart', P(None, 'P_Grey', 'M2.5,2.5 V21.5 H21.5'), E('B_Arrow', 'P_Arrow', 7, 16, 1.8), E('B_Arrow', 'P_Arrow', 11, 11, 1.8), E('B_Gold', 'P_Gold', 15, 13, 1.8), E('B_Gold', 'P_Gold', 18, 6, 1.8), E('B_Arrow', 'P_Arrow', 9, 7, 1.8))
G('IconDoughnutChart', E('B_Arrow', 'P_Arrow', 12, 12, 9.5), E('#FFFFFFFF', 'P_Arrow', 12, 12, 4.2), P('B_Gold', 'P_Gold', 'M12,2.5 A9.5,9.5 0 0 1 21.5,12 H16.2 A4.2,4.2 0 0 0 12,7.8 Z'))
G('IconProtectSheet', grid(2, 2.5, 14, 14, 3, 3, False), R('B_Gold', 'P_Gold', 13, 13, 9, 8.5, 1), P(None, 'P_Ring', 'M15,13 V11 C15,8 20,8 20,11 V13'))
G('IconGoTo', grid(2, 2.5, 14, 14, 3, 3, False), P('B_Green', 'P_Green', 'M11,15 H17 V12 L22.5,17 L17,22 V19 H11 Z'))
G('IconVTop', P(None, 'P_Tbl', 'M3,3 H21'), P(None, 'P_Bar', 'M7,7 H17 M7,10.5 H17 M9,14 H15'))
G('IconVMiddle', P(None, 'P_Bar', 'M7,8.5 H17 M7,12 H17 M9,15.5 H15'))
G('IconVBottom', P(None, 'P_Tbl', 'M3,21 H21'), P(None, 'P_Bar', 'M7,10 H17 M7,13.5 H17 M9,17 H15'))
G('IconSpecialPaste', R('B_Clip', 'P_ClipB', 3, 4, 15, 18, 1.5), R('B_Grey', 'P_Grey', 7, 2, 7, 4, 1), grid(10, 10, 11, 12, 2, 2, False))

xaml = ('<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"\n'
        '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">\n' + BRUSHES + extra + '\n'.join(out) + '\n</ResourceDictionary>\n')
open(os.path.join(HERE, '..', 'src', 'ValcoSpreadsheets', 'Themes', 'SheetIcons.xaml'), 'w').write(xaml)
print(len(out), 'icons')
