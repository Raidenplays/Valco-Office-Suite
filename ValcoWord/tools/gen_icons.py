# Generates src/ValcoWord/Themes/Icons.xaml (vector DrawingImages on a 24x24 grid, Vista style)
import os
HERE = os.path.dirname(os.path.abspath(__file__))
exec(open(os.path.join(HERE, '_icon_helpers.py')).read())

def page(x=4.5, y=2.5, w=15, h=19, fold=4.5, fill='#FFFFFFFF'):
    return (P(fill, 'P_Grey', f'M{x},{y} H{x+w-fold} L{x+w},{y+fold} V{y+h} H{x} Z') +
            P('B_Fold', 'P_Grey', f'M{x+w-fold},{y} V{y+fold} H{x+w} Z'))
def lines(x, y, w, n, gap=2.6, last=0.6):
    d = ''
    for i in range(n):
        ww = w * (last if i == n-1 and n > 1 else 1)
        d += f'M{x},{y+i*gap} H{x+ww} '
    return P(None, 'P_Text', d.strip())
def bars(widths_offsets, y0=4, gap=4):
    d = ''
    for i, (x, w) in enumerate(widths_offsets):
        d += f'M{x},{y0+i*gap} H{x+w} '
    return P(None, 'P_Bar', d.strip())

G('IconNew', page(), lines(7.5, 9, 9, 4), P('B_Gold', 'P_Gold', 'M18,0.8 L19.5,4.2 L23.2,4.5 L20.4,6.9 L21.2,10.5 L18,8.6 L14.8,10.5 L15.6,6.9 L12.8,4.5 L16.5,4.2 Z'))
G('IconDoc', page(), lines(7.5, 9, 9, 4))
G('IconOpen',
  P('B_FolderBack', 'P_Folder', 'M1.5,5.5 H8 L10,7.5 H19.5 V19.5 H1.5 Z'),
  page(6, 3, 11, 11, 3),
  P('B_Folder', 'P_Folder', 'M1.5,19.5 L4.5,10.5 H23 L20,19.5 Z'))
G('IconSave', R('B_Arrow', 'P_Arrow', 3, 3, 18, 18, 1.5), R('#FFFFFFFF', 'P_EnvThin', 6.5, 3.5, 11, 7), R('B_Grey', 'P_Grey', 8, 14, 8, 6.5), R('B_Arrow', None, 13.5, 15, 1.8, 4.5))
G('IconSaveAs', R('B_Arrow', 'P_Arrow', 2, 3, 16, 16, 1.5), R('#FFFFFFFF', 'P_EnvThin', 5, 3.5, 10, 6), R('B_Grey', 'P_Grey', 6.5, 12.5, 7, 6),
  P('B_Gold', 'P_Gold', 'M21.6,9.2 L23.6,11.2 L14,20.8 L11.2,21.6 L12,18.8 Z'))
G('IconPrint',
  R('#FFFFFFFF', 'P_Grey', 7, 2.5, 10, 7),
  R('B_Grey', 'P_Grey', 2.5, 8.5, 19, 9, 1.5),
  R('#FFFFFFFF', 'P_Grey', 6, 14.5, 12, 7),
  P(None, 'P_EnvThin', 'M8,17 H16 M8,19 H14'),
  E('B_Green', None, 18.8, 11, 1))
G('IconPreview', page(2.5, 2, 14, 18), lines(5, 8, 8, 4), E('B_Lens', 'P_Lens', 15, 15, 4.6), P(None, 'P_Handle', 'M18.4,18.4 L22.2,22.2'))
G('IconCut',
  E('#FFFFFFFF', 'P_Scissor', 6.5, 18, 3.2), E('#FFFFFFFF', 'P_Scissor', 17.5, 18, 3.2),
  P(None, 'P_Blade', 'M8.6,15.6 L16.5,2.5 M15.4,15.6 L7.5,2.5'), E('B_Grey', 'P_Grey', 12, 9.8, 1))
G('IconCopy', page(2.5, 2, 12, 15, 3.5), lines(4.8, 7, 6.5, 3, 2.4), page(9.5, 7, 12, 15, 3.5), lines(11.8, 12, 6.5, 4, 2.4))
G('IconPaste',
  R('B_Clip', 'P_ClipB', 3, 4, 15, 18, 1.5),
  R('B_Grey', 'P_Grey', 7, 2, 7, 4, 1),
  page(10, 9, 11, 13.5, 3), lines(12, 13.5, 6, 3, 2.4))
G('IconUndo', P('B_Arrow', 'P_Arrow', 'M2,9 L9,2.5 V6.2 C16,6 21.5,9.5 21.5,16 C21.5,19 20,21 18.5,22 C19.5,19 19,15.5 15.5,13.5 C13.5,12.4 11.5,12.2 9,12.3 V15.8 Z'))
G('IconRedo', P('B_Arrow', 'P_Arrow', 'M22,9 L15,2.5 V6.2 C8,6 2.5,9.5 2.5,16 C2.5,19 4,21 5.5,22 C4.5,19 5,15.5 8.5,13.5 C10.5,12.4 12.5,12.2 15,12.3 V15.8 Z'))
G('IconFind', E('B_Lens', 'P_Lens', 10, 10, 6.3), P(None, 'P_Handle', 'M14.8,14.8 L21,21'))
G('IconReplace', E('B_Lens', 'P_Lens', 8.5, 8.5, 5.3), P(None, 'P_Handle', 'M12.5,12.5 L15.5,15.5'),
  P('B_Green', 'P_Green', 'M13,20.5 H19 V18 L23.2,21.3 L19,24 V22.8 H13 Z'))
G('IconSpelling', P(None, 'P_Check', 'M3.5,12.5 L8.5,17.5 L20.5,4.5'), P(None, 'P_Wavy', 'M3,21.5 Q5,19.5 7,21.5 T11,21.5 T15,21.5 T19,21.5'))
G('IconPicture', R('B_Frame', 'P_Grey', 2, 4, 20, 16, 1), R('B_Sky', None, 3.5, 5.5, 17, 13),
  P('B_Land', None, 'M3.5,18.5 L9,11.5 L13,15.5 L16,13 L20.5,18.5 Z'), E('B_Gold', None, 16.5, 8.8, 1.8))
G('IconTable', R('#FFFFFFFF', 'P_Tbl', 2.5, 3.5, 19, 17), R('B_TblHead', 'P_Tbl', 2.5, 3.5, 19, 4.5),
  P(None, 'P_Tbl', 'M2.5,12.2 H21.5 M2.5,16.4 H21.5 M8.8,3.5 V20.5 M15.2,3.5 V20.5'))
G('IconLink', P(None, 'P_Chain', 'M10.5,13.5 C9,12 9,10 10.5,8.5 L13.5,5.5 C15,4 17.5,4 19,5.5 C20.5,7 20.5,9.5 19,11 L17.5,12.5'),
  P(None, 'P_Chain', 'M13.5,10.5 C15,12 15,14 13.5,15.5 L10.5,18.5 C9,20 6.5,20 5,18.5 C3.5,17 3.5,14.5 5,13 L6.5,11.5'))
G('IconDate', R('#FFFFFFFF', 'P_Grey', 3, 4, 18, 17, 1.5), R('B_Red', 'P_Red', 3, 4, 18, 5, 1.5),
  P(None, 'P_Ring', 'M7.5,2.5 V6 M16.5,2.5 V6'),
  P(None, 'P_GreyThin', 'M6,12 H8 M11,12 H13 M16,12 H18 M6,15.5 H8 M11,15.5 H13 M16,15.5 H18 M6,19 H8 M11,19 H13'))
G('IconSymbol', P('B_Arrow', 'P_Arrow', 'M3.5,21 V17.8 H7.6 C4.5,15.8 3,13.2 3,10.4 C3,5.6 7,2.5 12,2.5 C17,2.5 21,5.6 21,10.4 C21,13.2 19.5,15.8 16.4,17.8 H20.5 V21 H13.6 V17.2 C16.2,15.8 17.4,13.4 17.4,10.6 C17.4,7.4 15.2,5.6 12,5.6 C8.8,5.6 6.6,7.4 6.6,10.6 C6.6,13.4 7.8,15.8 10.4,17.2 V21 Z'))
G('IconPageBreak', P('#FFFFFFFF', 'P_Grey', 'M4.5,2 H19.5 V9 H4.5 Z'), P('#FFFFFFFF', 'P_Grey', 'M4.5,15 H19.5 V22 H4.5 Z'),
  P(None, 'P_Dash', 'M1.5,12 H22.5'))
G('IconHLine', P(None, 'P_Text', 'M4,5 H20 M4,8 H16'), P(None, 'P_Rule', 'M2,12.5 H22'), P(None, 'P_Text', 'M4,17 H20 M4,20 H14'))
G('IconAlignLeft', bars([(3, 18), (3, 12), (3, 18), (3, 10)]))
G('IconAlignCenter', bars([(3, 18), (6, 12), (3, 18), (7, 10)]))
G('IconAlignRight', bars([(3, 18), (9, 12), (3, 18), (11, 10)]))
G('IconAlignJustify', bars([(3, 18), (3, 18), (3, 18), (3, 18)]))
G('IconBullets', E('B_Arrow', None, 4.5, 5.5, 1.8), E('B_Arrow', None, 4.5, 12, 1.8), E('B_Arrow', None, 4.5, 18.5, 1.8),
  P(None, 'P_Bar', 'M9,5.5 H21 M9,12 H21 M9,18.5 H21'))
G('IconNumbering', P(None, 'P_Num', 'M3.5,3.8 L5,3 V8.5 M2.9,10.6 C3.4,9.6 6.2,9.6 6.2,11.2 C6.2,12.6 3,13.6 3,15 H6.4 M3,17 H6 L4.3,18.8 C6.6,18.6 6.8,22 3,21.4'),
  P(None, 'P_Bar', 'M9,5.5 H21 M9,12 H21 M9,18.5 H21'))
G('IconIndent', P(None, 'P_Bar', 'M3,4 H21 M11,8 H21 M11,12 H21 M11,16 H21 M3,20 H21'), P('B_Arrow', 'P_Arrow', 'M3,8.5 L8,12 L3,15.5 Z'))
G('IconOutdent', P(None, 'P_Bar', 'M3,4 H21 M11,8 H21 M11,12 H21 M11,16 H21 M3,20 H21'), P('B_Arrow', 'P_Arrow', 'M8,8.5 L3,12 L8,15.5 Z'))
G('IconLineSpacing', P(None, 'P_Bar', 'M10,5 H21 M10,10 H21 M10,15 H21 M10,20 H21'),
  P('B_Arrow', 'P_Arrow', 'M4.5,2 L7.5,6 H5.5 V18 H7.5 L4.5,22 L1.5,18 H3.5 V6 H1.5 Z'))
G('IconFontColor', P('#FF1F1F1F', None, 'M10.4,2.5 H13.6 L19.5,17.5 H16.4 L15,13.6 H9 L7.6,17.5 H4.5 Z M9.9,11 H14.1 L12,5.2 Z'))
G('IconHighlight', P('B_Gold', 'P_Gold', 'M14.5,2.5 L19.5,7.5 L11,16 L6,11 Z'), P('B_Grey', 'P_Grey', 'M6,11 L11,16 L9,18 H5 L4,17 V13 Z'),
  P('#FF404040', None, 'M4,17 L5,18 L3.2,19.2 L2.6,18.6 Z'))
G('IconClearFormat', P('#FF1F1F1F', None, 'M7.4,2.5 H10.6 L15,13.5 H12.1 L11,10.6 H7 L5.9,13.5 H3 Z M7.8,8.4 H10.2 L9,5 Z'),
  P('B_Eraser', 'P_Red', 'M16.5,11 L22,16.5 L16.5,22 H12 L9.8,19.8 Z'), P('#FFFFFFFF', 'P_Red', 'M9.8,19.8 L13.2,16.4 L17.4,20.6 L16,22 H12 Z'))
G('IconZoom', E('B_Lens', 'P_Lens', 10, 10, 6.3), P(None, 'P_Handle', 'M14.8,14.8 L21,21'), P(None, 'P_Plus', 'M7,10 H13 M10,7 V13'))
G('IconWordCount', page(), P('#FF2B5797', None, 'M7,8 H10 V9.5 H9.2 V16 H7.8 V9.5 H7 Z M11,9 H17 V10.4 H11 Z M11,12 H17 V13.4 H11 Z M11,15 H15 V16.4 H11 Z'))
G('IconPageSetup', page(3.5, 2, 15, 19), P(None, 'P_Dash2', 'M6.5,5 H14 M6.5,18 H15.5 M6.5,5 V18 M15.5,8 V18'),
  E('B_Grey', 'P_Grey', 18, 18, 4.2), E('#FFFFFFFF', 'P_Grey', 18, 18, 1.6))
G('IconPdf', page(), R('B_Red', 'P_Red', 2.5, 12, 15, 7, 1), P('#FFFFFFFF', None, 'M4.3,13.5 H6.6 C8,13.5 8,16 6.6,16 H5.4 V17.6 H4.3 Z M5.4,14.4 V15.1 H6.4 C6.9,15.1 6.9,14.4 6.4,14.4 Z M9,13.5 H10.5 C12.6,13.5 12.6,17.6 10.5,17.6 H9 Z M10.1,14.4 V16.7 H10.4 C11.4,16.7 11.4,14.4 10.4,14.4 Z M13.2,13.5 H15.8 V14.4 H14.3 V15.1 H15.6 V16 H14.3 V17.6 H13.2 Z'))
G('IconParagraph', P('#FF2B5797', None, 'M10,3 H19 V5 H17.2 V21 H15.2 V5 H13.2 V21 H11.2 V13.5 H10 C6.5,13.5 4.5,11.4 4.5,8.3 C4.5,5.1 6.5,3 10,3 Z'))
G('IconFont', P('#FF2B5797', None, 'M8.5,3 H11.5 L18,20 H14.8 L13.3,16 H6.7 L5.2,20 H2 Z M7.6,13.4 H12.4 L10,6.7 Z'),
  P('#FF8A1C10', None, 'M17,13 H19 L22.5,21 H20.8 L20.1,19.2 H16.9 L16.2,21 H14.5 Z M17.4,17.9 H19.6 L18.5,15 Z'))
G('IconProperties', R('#FFFFFFFF', 'P_Grey', 3.5, 2.5, 17, 19, 1), P(None, 'P_GreyThin', 'M7,7 H17 M7,10.5 H17 M7,14 H17 M7,17.5 H13'), E('B_Arrow', 'P_Arrow', 18, 18, 4.5), R('#FFFFFFFF', None, 17.3, 16.6, 1.4, 3.8))
G('IconInfo', E('B_Arrow', 'P_Arrow', 12, 12, 10.5), E('#FFFFFFFF', None, 12, 6.8, 1.7), R('#FFFFFFFF', None, 10.4, 9.8, 3.2, 9, 0.8))
G('IconQuestion', E('B_Arrow', 'P_Arrow', 12, 12, 10.5), P(None, 'P_WhiteFat', 'M8.8,9.2 C8.8,5.4 15.2,5.4 15.2,9.2 C15.2,12 12,11.8 12,14.8'), E('#FFFFFFFF', None, 12, 18.2, 1.6))
G('IconWarning', P('B_Gold', 'P_Gold', 'M12,1.8 L23,21.6 H1 Z'), R('#FF000000', None, 10.9, 8, 2.2, 7.5, 1), E('#FF000000', None, 12, 18.3, 1.4))
G('IconError', E('B_Red', 'P_Red', 12, 12, 10.5), P(None, 'P_WhiteFat', 'M8,8 L16,16 M16,8 L8,16'))
G('IconApp',
  E('B_AppDisc', 'P_AppDisc', 12, 12, 11),
  P('B_AppShine', None, 'M3.2,10 C3.5,5 7.5,1.8 12,1.8 C16.5,1.8 20.5,5 20.8,10 C17,8.2 7,8.2 3.2,10 Z'),
  page(6.5, 5, 11, 14, 3.2), lines(8.5, 10, 6, 3, 2.3))


def pg(x=4.5, y=2.5, w=15, h=19, fold=4.5): return page(x, y, w, h, fold)
def txt(x, y, w, n, gap=2.6): return lines(x, y, w, n, gap)
G('IconBlank', R('#00FFFFFF', None, 0, 0, 1, 1))
G('IconFormatPainter', R('B_Gold','P_Gold',3,2.5,14,6,1.2), P(None,'P_Grey','M17,5.5 H20 V11 H11 V14'), R('B_Grey','P_Grey',9.5,14,3,8,1))
G('IconSort', P(None,'P_Num','M3,9 L5.5,3 L8,9 M3.8,7 H7.2 M3,13 H8 L3,20 H8'), P('B_Arrow','P_Arrow','M14.5,3 H17.5 V16 H20.5 L16,21.5 L11.5,16 H14.5 Z'))
G('IconShading', P('B_Arrow','P_Arrow','M6,10 L12,3.5 L18.5,10 L12,16.5 Z'), P('B_Gold','P_Gold','M19,12 C21,15 21.5,17 19.8,18 C18.2,18.8 17,17 19,12 Z'), R('B_Gold',None,2,19.5,20,3))
G('IconBorders', R('#FFFFFFFF',None,3,3,18,18), P(None,'P_Dash2','M3,12 H21 M12,3 V21'), P(None,'P_Bar','M3,21 H21'))
G('IconMultilevel', P(None,'P_Num','M2.5,3.5 L4,3 V7.5'), P(None,'P_Bar','M7,5 H21 M10,12 H21 M13,19 H21'), E('B_Arrow',None,7.3,12,1.4), R('B_Arrow',None,9.5,17.8,2.4,2.4))
G('IconChangeStyles', R('#FFFFFFFF','P_Grey',2.5,4,19,16,1), P('#FF2B5797',None,'M5,16 L8,7 H9.6 L12.6,16 H11 L10.3,14 H7.3 L6.6,16 Z M7.8,12.6 H9.8 L8.8,9.6 Z'), P('#FFC0504D',None,'M13.5,16 V7 H16.5 C19,7 19,11 16.8,11.3 C19.5,11.6 19.5,16 16.6,16 Z M15,8.3 V10.7 H16.3 C17.6,10.7 17.6,8.3 16.3,8.3 Z M15,12 V14.7 H16.5 C18,14.7 18,12 16.5,12 Z'))
G('IconSelect', P('#FFFFFFFF','P_Grey','M6,2.5 L6,19 L10,15 L13,21.5 L15.5,20.3 L12.6,14 L18,14 Z'))
G('IconCoverPage', pg(4,2,16,20,4), R('B_Arrow',None,4.5,7,15,6), P(None,'P_WhiteFat','M7,10 H14'), txt(7,16,10,2,2.4))
G('IconBlankPage', pg(5,2.5,14,19,4), P('B_Green','P_Green','M17.5,13 H20 V16 H23 V18.5 H20 V21.5 H17.5 V18.5 H14.5 V16 H17.5 Z'))
G('IconShapes', E('B_Arrow','P_Arrow',8,8,5.5), R('B_Green','P_Green',10,10,11,10,1), P('B_Gold','P_Gold','M5,21.5 L9,14 L13,21.5 Z'))
G('IconClipArt', R('B_Frame','P_Grey',2.5,3.5,19,17,1), E('B_Gold','P_Gold',9,10,3.3), P('B_Red','P_Red','M12,18.5 C12,14 17,12.5 19,9 C19.5,13 18,17.5 12,18.5 Z'), P('B_Green','P_Green','M4.5,18.5 C6,15 9,14.5 11,16 C9,17 7,17.5 4.5,18.5 Z'))
G('IconSmartArt', R('B_Arrow','P_Arrow',2,8.5,6,6,1), R('B_Green','P_Green',9,8.5,6,6,1), R('B_Gold','P_Gold',16,8.5,6,6,1), P(None,'P_GreyThin','M8,11.5 H9 M15,11.5 H16'))
G('IconChart', P(None,'P_Grey','M3,3 V21 H21.5'), R('B_Arrow','P_Arrow',5.5,12,3.5,9), R('B_Green','P_Green',10.5,7,3.5,14), R('B_Red','P_Red',15.5,10,3.5,11))
G('IconBookmark', pg(3,2.5,15,19,4), P('B_Red','P_Red','M12.5,1.5 H19 V13 L15.75,10.2 L12.5,13 Z'))
G('IconCrossRef', pg(), P('B_Arrow','P_Arrow','M8,13 H13 V10 L18,14.5 L13,19 V16 H8 Z'))
G('IconHeader', pg(), R('B_TblHead','P_Tbl',6,4.5,12,4), txt(7.5,12,9,3))
G('IconFooter', pg(), txt(7.5,6,9,3), R('B_TblHead','P_Tbl',6,15.5,12,4))
G('IconPageNumber', pg(), txt(7.5,6,9,3), P('#FF2B5797',None,'M10.5,15.2 L12.6,14 H13.8 V20.5 H12.4 V15.8 L11,16.6 Z'))
G('IconTextBox', R('#FFFFFFFF','P_Tbl',2.5,4,19,16), P('#FF2B5797',None,'M7,8 H17 V10 H13 V17 H11 V10 H7 Z'))
G('IconQuickParts', R('B_Arrow','P_Arrow',2.5,3,9,8,1), R('B_Green','P_Green',12.5,3,9,8,1), R('B_Gold','P_Gold',2.5,13,9,8,1), R('B_Red','P_Red',12.5,13,9,8,1))
G('IconWordArt', P('B_WordArt','P_Arrow','M3,20 L9.5,3.5 H13 L19.5,20 H15.8 L14.3,16 H8.1 L6.6,20 Z M9.3,12.8 H13.1 L11.2,7.4 Z'))
G('IconDropCap', P('#FF2B5797',None,'M2.5,3 H11 V5 H8 V13 H5.5 V5 H2.5 Z'), txt(13,4,8,3,4), txt(3,16.5,18,2,4))
G('IconSignature', R('#FFFFFFFF','P_Grey',2,5,20,14,1), P(None,'P_Num','M4.5,13 C6,9 7,9 7.5,12 C8,15 9.5,9.5 11,11 C12,12 12.5,12.5 14,10.5'), P(None,'P_Grey','M4,16 H20'), P(None,'P_Red','M4,14 L6,16 M6,14 L4,16'))
G('IconObject', R('#FFFFFFFF','P_Grey',3,3,18,18,1), R('B_Arrow','P_Arrow',6,6,6,6), E('B_Green','P_Green',15,15,3.5))
G('IconEquation', P('#FF2B5797',None,'M5,4 H17 V6.5 L15.5,6.2 C15,5.4 14.5,5.5 13.8,5.5 H9 L13.5,12 L8.8,18.5 H14.2 C15,18.5 15.6,18.4 16,17.5 L17.6,17.2 L17,20 H5 V19.4 L10.4,12 L5,4.6 Z'))
G('IconThemes', E('B_Arrow','P_Arrow',8,8,5.5), E('B_Gold','P_Gold',16,8,5.5), E('B_Green','P_Green',12,15.5,5.5))
G('IconMargins', pg(3.5,2,16,20,0.01), P(None,'P_Dash','M7,2 V22 M16,2 V22 M3.5,5.5 H19.5 M3.5,18.5 H19.5'))
G('IconOrientation', pg(2,7,13,15,3), pg(9,2,13,10,3), P('B_Arrow','P_Arrow','M17,13 C17,17 15,19 12,19 V17 L8.5,20 L12,23 V21 C16,21 19,18 19,13 Z'))
G('IconSize', pg(2,7,11,15,3), pg(9,2,13,18,3))
G('IconColumns', pg(3,2,18,20,0.01), txt(5.5,5,6,6,2.4), txt(13,5,6,6,2.4))
G('IconBreaks', pg(4,2,16,8,0.01), pg(4,14,16,8,0.01), P(None,'P_Dash','M1.5,12 H22.5'), P('B_Arrow','P_Arrow','M8,10.5 L12,13.5 L16,10.5 Z'))
G('IconLineNumbers', pg(6,2,15,20,4), P(None,'P_Num','M2,5 L3.5,4.4 V9 M1.7,12 C2,10.8 4.6,10.9 4.6,12.4 C4.6,13.8 1.8,14.6 1.8,16 H4.8'), txt(8.5,6,10,6))
G('IconHyphenation', pg(), P('#FF2B5797',None,'M8,10 H12 V12 H8 Z'), txt(7.5,6,9,1), txt(7.5,15,9,3))
G('IconWatermark', pg(), P('B_Wm',None,'M6,17 L16.5,6.5 L18,8 L7.5,18.5 Z'))
G('IconPageColor', pg(3,2,16,20,4), P('B_Gold','P_Gold','M12,9 C15,13 17,15.5 15,18 C13,20 9,19 9,16 C9,14 10.5,11.5 12,9 Z'))
G('IconPageBorders', pg(3,2,18,20,0.01), R(None,'P_Bar',5.5,4.5,13,15), txt(8,8,8,3))
G('IconTOC', pg(), P(None,'P_Num','M7,7 H12 M7,10.5 H13 M7,14 H11 M7,17.5 H12'), P(None,'P_Dots','M13,7 H16 M14,10.5 H16 M12,14 H16 M13,17.5 H16'))
G('IconFootnote', pg(), txt(7,6,9,3), P(None,'P_GreyThin','M7,15 H11'), P('#FF2B5797',None,'M7,17 H8.5 V20 H7.5 V18 H7 Z'), txt(10,18.5,6,1))
G('IconEndnote', pg(), txt(7,6,9,4), P('#FF2B5797',None,'M7,16.5 H8.5 V20 H7.5 V17.5 H7 Z'), txt(10,18,6,1))
G('IconCitation', pg(), txt(7.5,6,9,3), P('#FF2B5797',None,'M7,14 C6,15 6,17 7,18 M15,14 C16,15 16,17 15,18'), txt(8.5,16,5,1))
G('IconBibliography', R('B_Arrow','P_Arrow',3,4,6,17,1), R('B_Green','P_Green',9.5,3,5.5,18,1), R('B_Red','P_Red',15.5,5,5.5,16,1))
G('IconManageSources', R('B_Arrow','P_Arrow',2.5,5,6,15,1), R('B_Green','P_Green',9,4,5.5,16,1), P('B_Gold','P_Gold','M21.6,9.2 L23.6,11.2 L14,20.8 L11.2,21.6 L12,18.8 Z'))
G('IconCaption', R('B_Frame','P_Grey',3,2.5,18,12,1), R('B_Sky',None,4.5,4,15,9), txt(5,18.5,14,2,2.6))
G('IconIndex', pg(), P('#FF2B5797',None,'M7,6 H9 V12 H7 Z'), txt(10.5,7,6,2,3), P('#FF2B5797',None,'M7,14 H9 V20 H7 Z'), txt(10.5,15,6,2,3))
G('IconMarkEntry', pg(), txt(7.5,6,9,3), R('B_Gold',None,7,13.5,10,3), txt(7.5,19,7,1))
G('IconTOA', pg(), P('#FF2B5797',None,'M12,5 V19 M8,7 H16 M8,7 L6,12 H10 Z M16,7 L14,12 H18 Z M9,19 H15'))
G('IconEnvelope', env(2,5,20,14))
G('IconLabels', R('#FFFFFFFF','P_Grey',3,2,18,20,1), R('B_TblHead','P_Tbl',5,4,6,4,1), R('B_TblHead','P_Tbl',13,4,6,4,1), R('B_TblHead','P_Tbl',5,10,6,4,1), R('B_TblHead','P_Tbl',13,10,6,4,1), R('B_TblHead','P_Tbl',5,16,6,4,1), R('B_TblHead','P_Tbl',13,16,6,4,1))
G('IconMailMerge', pg(2,4,12,16,3), env(11,3,11,8), env(11,13,11,8))
G('IconRecipients', E('B_Skin','P_Skin',8,7,3.5), P('B_Arrow','P_Arrow','M2,20 C2,15 4.5,12.5 8,12.5 C11.5,12.5 14,15 14,20 Z'), E('B_Skin','P_Skin',16.5,8,3), P('B_Green','P_Green','M12,20 C12,16 14,13.5 16.5,13.5 C19.5,13.5 22,16 22,20 Z'))
G('IconMergeField', R('#FFFFFFFF','P_Grey',2,6,20,12,1), P('#FF2B5797',None,'M5,9 L3.5,12 L5,15 H6 L4.5,12 L6,9 Z M8,9 L6.5,12 L8,15 H9 L7.5,12 L9,9 Z M16,9 L17.5,12 L16,15 H15 L16.5,12 L15,9 Z M19,9 L20.5,12 L19,15 H18 L19.5,12 L18,9 Z'), P(None,'P_GreyThin','M10,12 H14'))
G('IconPreviewResults', R('#FFFFFFFF','P_Grey',2,6,14,12,1), P('#FF2B5797',None,'M4.5,10 H13 M4.5,13 H11'), E('B_Lens','P_Lens',16,15,4.5), P(None,'P_Handle','M19.3,18.3 L22,21'))
G('IconFinishMerge', pg(2,2,12,16,3), pg(6,5,12,16,3), P('B_Green','P_Green','M14,14 L17,17 L22,10 L23.5,11.5 L17,20 L12.5,15.5 Z'))
G('IconResearch', R('B_Arrow','P_Arrow',2,3,8,18,1), R('B_Green','P_Green',8.5,4,6,16,1), E('B_Lens','P_Lens',16,13,4.5), P(None,'P_Handle','M19.3,16.3 L22,19'))
G('IconThesaurus', R('B_Red','P_Red',3,3,15,18,1.5), R('#FFFFFFFF',None,5.5,5,10,14), P('#FF2B5797',None,'M7,9 L9,13 L11,9 H12 L9.5,15 H8.5 L6,9 Z'), txt(12,13,3,1))
G('IconTranslate', R('B_Arrow','P_Arrow',2,2,12,12,1.5), P('#FFFFFFFF',None,'M5,10.5 L7.5,4.5 H9 L11.5,10.5 H10 L9.4,9 H7.1 L6.5,10.5 Z M7.6,7.8 H8.9 L8.3,6 Z'), R('B_Green','P_Green',10,10,12,12,1.5), P('#FFFFFFFF',None,'M13,13 H19 M16,12 V13 M14,14 C15,17 17,18.5 19,19 M18,14 C17,17 15,18.5 13,19'))
G('IconComment', P('B_Gold','P_Gold','M2.5,3.5 H21.5 V15.5 H11 L6,20.5 V15.5 H2.5 Z'), P(None,'P_GreyThin','M5.5,7.5 H18.5 M5.5,11 H15'))
G('IconDeleteComment', P('B_Gold','P_Gold','M2.5,3.5 H19 V13.5 H10 L6,17.5 V13.5 H2.5 Z'), P('B_Red','P_Red','M14,15.5 L15.5,14 L18,16.5 L20.5,14 L22,15.5 L19.5,18 L22,20.5 L20.5,22 L18,19.5 L15.5,22 L14,20.5 L16.5,18 Z'))
G('IconPrevComment', P('B_Gold','P_Gold','M7.5,3.5 H21.5 V15.5 H14 L10,19.5 V15.5 H7.5 Z'), P('B_Arrow','P_Arrow','M1.5,11 L6.5,6 V16 Z'))
G('IconNextComment', P('B_Gold','P_Gold','M2.5,3.5 H16.5 V15.5 H9 L5,19.5 V15.5 H2.5 Z'), P('B_Arrow','P_Arrow','M22.5,11 L17.5,6 V16 Z'))
G('IconTrack', pg(), P(None,'P_Red','M7,8 H17'), P(None,'P_Strike','M7,12 H15'), P('#FFD21E10',None,'M6,15.5 H17 V17 H6 Z'), P(None,'P_Num','M2,3 V21'))
G('IconAccept', pg(), txt(7.5,6,9,3), P('B_Green','P_Green','M8,16 L11,19 L18,11 L19.5,12.5 L11,22 L6.5,17.5 Z'))
G('IconReject', pg(), txt(7.5,6,9,3), P('B_Red','P_Red','M8,14 L9.5,12.5 L12.5,15.5 L15.5,12.5 L17,14 L14,17 L17,20 L15.5,21.5 L12.5,18.5 L9.5,21.5 L8,20 L11,17 Z'))
G('IconPrevChange', pg(6,2,15,20,4), P('B_Arrow','P_Arrow','M1.5,12 L7,6.5 V17.5 Z'))
G('IconNextChange', pg(3,2,15,20,4), P('B_Arrow','P_Arrow','M22.5,12 L17,6.5 V17.5 Z'))
G('IconCompare', pg(1.5,3,11,16,3), pg(11.5,3,11,16,3), P('#FFD21E10',None,'M3.5,9 H9 V10.5 H3.5 Z M13.5,13 H19 V14.5 H13.5 Z'))
G('IconProtect', R('B_Gold','P_Gold',4.5,10,15,11.5,1.5), P(None,'P_Pole','M8,10 V7 C8,2 16,2 16,7 V10'), E('#FF5A4127',None,12,15,1.6))
G('IconMarkFinal', pg(), P('B_Red','P_Red','M12,9 L13.6,12.5 L17.5,13 L14.6,15.6 L15.4,19.5 L12,17.5 L8.6,19.5 L9.4,15.6 L6.5,13 L10.4,12.5 Z'))
G('IconInspect', pg(), E('B_Lens','P_Lens',12.5,13,4.5), P(None,'P_Handle','M15.8,16.3 L19,19.5'))
G('IconPrintLayout', pg(4,2,16,20,4), txt(7,6,10,6))
G('IconFullScreen', R('B_Arrow','P_Arrow',2,4,20,16,1), R('#FFFFFFFF',None,3.5,5.5,8,13), R('#FFFFFFFF',None,12.5,5.5,8,13), txt(4.8,8,5.5,4,2.4), txt(13.8,8,5.5,4,2.4))
G('IconWebLayout', E('B_Globe','P_Globe',12,12,9.5), P('B_Land',None,'M6,7 C8,5 11,6 10.5,9 C10,11 7.5,10.5 7,13 C6.5,15 4.5,13 4,11 C3.8,9.5 4.8,8 6,7 Z M13,4.5 C15.5,4 18,6 17.5,8 C16.5,9.5 14,8.5 13.5,10.5 C13,12 15,13 16.5,13.5 C18,14 18.5,16 17,17.5 C15.5,18.5 14,17 13.5,15.5 C13,14 11.5,13.5 11.5,11.5 C11.5,9.5 12,6 13,4.5 Z'))
G('IconOutline', P(None,'P_Num','M3,5 H14 M7,9.5 H18 M7,14 H18 M3,18.5 H14'), E('B_Arrow',None,4,9.5,1.3), E('B_Arrow',None,4,14,1.3))
G('IconDraft', R('#FFFFFFFF','P_Grey',2,3,20,18,0.5), txt(4.5,7,15,5,2.6))
G('IconDocMap', R('#FFFFFFFF','P_Grey',2,3,20,18,1), R('B_TblHead','P_Tbl',2,3,7,18), P(None,'P_GreyThin','M3.5,6 H7.5 M4.5,9 H7.5 M4.5,12 H7.5 M3.5,15 H7.5'), txt(11,7,9,4))
G('IconThumbnails', R('#FFFFFFFF','P_Grey',2,3,20,18,1), R('B_TblHead','P_Tbl',2,3,7,18), R('#FFFFFFFF','P_GreyThin',3.5,4.5,4,5), R('#FFFFFFFF','P_GreyThin',3.5,11,4,5), txt(11,7,9,4))
G('IconGridlines', R('#FFFFFFFF','P_Grey',2,2,20,20), P(None,'P_Dash2','M2,7 H22 M2,12 H22 M2,17 H22 M7,2 V22 M12,2 V22 M17,2 V22'))
G('IconOnePage', pg(6,2,12,20,3), txt(8.2,6,7,6,2.3))
G('IconTwoPages', pg(1.5,3,10,17,3), pg(12.5,3,10,17,3))
G('IconPageWidth', pg(2,2,20,20,4), P('B_Arrow','P_Arrow','M3,12 L7,8.5 V10.8 H17 V8.5 L21,12 L17,15.5 V13.2 H7 V15.5 Z'))
G('IconArrange', R('#FFFFFFFF','P_Tbl',2,2.5,20,9), R('B_TblHead',None,2,2.5,20,2.5), R('#FFFFFFFF','P_Tbl',2,12.5,20,9), R('B_TblHead',None,2,12.5,20,2.5))
G('IconSideBySide', R('#FFFFFFFF','P_Tbl',1.5,3,10,18), R('B_TblHead',None,1.5,3,10,3), R('#FFFFFFFF','P_Tbl',12.5,3,10,18), R('B_TblHead',None,12.5,3,10,3))
G('IconSwitchWindows', R('#FFFFFFFF','P_Tbl',2,2,14,12), R('B_TblHead',None,2,2,14,3), R('#FFFFFFFF','P_Tbl',8,10,14,12), R('B_TblHead',None,8,10,14,3))
G('IconTableStyle', R('#FFFFFFFF','P_Tbl',2,3.5,20,17), R('B_Arrow',None,2,3.5,20,4), R('B_TblHead',None,2,12,20,4), P(None,'P_Tbl','M2,7.5 H22 M2,12 H22 M2,16 H22 M8.6,3.5 V20.5 M15.3,3.5 V20.5'))
G('IconMerge', R('#FFFFFFFF','P_Tbl',2,5,20,14), P('B_Arrow','P_Arrow','M4,12 L7.5,9 V11 H10 V13 H7.5 V15 Z M20,12 L16.5,9 V11 H14 V13 H16.5 V15 Z'))
G('IconSplit', R('#FFFFFFFF','P_Tbl',2,5,20,14), P(None,'P_Tbl','M12,5 V19'), P('B_Arrow','P_Arrow','M3.5,12 L7,9 V15 Z M20.5,12 L17,9 V15 Z'))
G('IconFormula', P('#FF2B5797',None,'M6,4 H18 V6 H9.5 L14,12 L9.5,18 H18 V20 H6 V18.5 L11,12 L6,5.5 Z'))
G('IconConvert', R('#FFFFFFFF','P_Tbl',2,2,9,9), P(None,'P_Tbl','M2,6.5 H11 M6.5,2 V11'), txt(13,15,9,3,2.4), P('B_Arrow','P_Arrow','M13,4 H17 V2 L21,5.5 L17,9 V7 H13 Z'))
G('IconBrightness', E('B_Gold','P_Gold',12,12,5), P(None,'P_Sun','M12,2 V4.5 M12,19.5 V22 M2,12 H4.5 M19.5,12 H22 M5,5 L6.8,6.8 M17.2,17.2 L19,19 M5,19 L6.8,17.2 M17.2,6.8 L19,5'))
G('IconContrast', E('#FFFFFFFF','P_Bar',12,12,9), P('#FF1F1F1F',None,'M12,3 A9,9 0 0 1 12,21 Z'))
G('IconRecolor', R('B_Frame','P_Grey',2,3,20,18,1), R('B_Gray',None,3.5,4.5,8.5,15), R('B_Sky',None,12,4.5,8.5,15))
G('IconRotate', R('B_Arrow','P_Arrow',6,9,12,12,1), P(None,'P_Num','M5,7 C7,3 13,2 17,4.5'), P('B_Arrow',None,'M17.5,1.5 L19.5,6.5 L14.5,6.8 Z'))
G('IconCrop', P(None,'P_Bar','M6,2 V18 H22 M2,6 H18 V22'))
G('IconWrap', R('B_Frame','P_Grey',2.5,6,9,9), txt(13.5,6,8,4,3), txt(2.5,18,19,2,3))
G('IconPictureStyle', R('#FFFFFFFF','P_Grey',2,3,20,18,2), R('B_Sky',None,4.5,5.5,15,13), P('B_Land',None,'M4.5,18.5 L10,12 L13.5,15.5 L16,13.5 L19.5,18.5 Z'))
G('IconResetPicture', R('B_Frame','P_Grey',2,4,16,13,1), R('B_Sky',None,3.5,5.5,13,10), P('B_Arrow','P_Arrow','M13,22 C19,22 22,18 21,13 L23,13 L20,9 L17,13 L19,13 C20,16.5 18,19.5 13,19.5 Z'))
G('IconOptions', R('#FFFFFFFF','P_Grey',2.5,3,19,18,1), E('B_Grey','P_Grey',12,12,5.5), E('#FFFFFFFF','P_Grey',12,12,2))
G('IconExit', R('#FFFFFFFF','P_Grey',3,3,10,18,1), P('B_Red','P_Red','M11,10 H17 V7 L22,12 L17,17 V14 H11 Z'))
G('IconPromote', P('B_Green','P_Green','M3,12 L10,5 V9.5 H21 V14.5 H10 V19 Z'))
G('IconDemote', P('B_Arrow','P_Arrow','M21,12 L14,5 V9.5 H3 V14.5 H14 V19 Z'))
G('IconMoveUp', P('B_Arrow','P_Arrow','M12,3 L19.5,11 H15 V21 H9 V11 H4.5 Z'))
G('IconMoveDown', P('B_Arrow','P_Arrow','M12,21 L19.5,13 H15 V3 H9 V13 H4.5 Z'))
G('IconDelete', P('B_Red','P_Red','M4,6.5 L6.5,4 L12,9.5 L17.5,4 L20,6.5 L14.5,12 L20,17.5 L17.5,20 L12,14.5 L6.5,20 L4,17.5 L9.5,12 Z'))
G('IconRefresh', P('B_Green','P_Green','M12,3 C17,3 21,7 21,12 L23,12 L19.5,16.5 L16,12 L18,12 C18,8.5 15.5,6 12,6 Z M12,21 C7,21 3,17 3,12 L1,12 L4.5,7.5 L8,12 L6,12 C6,15.5 8.5,18 12,18 Z'))
G('IconNextRecord', P('B_Arrow','P_Arrow','M6,4 L17,12 L6,20 Z'))
G('IconPrevRecord', P('B_Arrow','P_Arrow','M18,4 L7,12 L18,20 Z'))
G('IconFirstRecord', P('B_Arrow','P_Arrow','M19,4 L9,12 L19,20 Z'), R('B_Arrow','P_Arrow',4.5,4,3,16))
G('IconLastRecord', P('B_Arrow','P_Arrow','M5,4 L15,12 L5,20 Z'), R('B_Arrow','P_Arrow',16.5,4,3,16))
G('IconMacro', R('#FFFFFFFF','P_Grey',2.5,3,19,18,1), P('B_Green','P_Green','M8,7 L17,12 L8,17 Z'))
G('IconSplitWindow', R('#FFFFFFFF','P_Tbl',2,2,20,20), P(None,'P_Bar','M2,12 H22'), txt(4,5.5,14,2,2.6), txt(4,15.5,14,2,2.6))
G('IconNewWindow', R('#FFFFFFFF','P_Tbl',2,5,15,15), R('B_TblHead',None,2,5,15,3), P('B_Gold','P_Gold','M18,0.8 L19.5,4.2 L23.2,4.5 L20.4,6.9 L21.2,10.5 L18,8.6 L14.8,10.5 L15.6,6.9 L12.8,4.5 L16.5,4.2 Z'))
G('IconColumnBreak', R('#FFFFFFFF','P_Grey',2,3,20,18), txt(4,6,6,5,2.6), P(None,'P_Dash','M12,3 V21'), txt(14,6,6,2,2.6))
G('IconAddText', P(None,'P_Num','M3,5 H14 M7,10 H18 M11,15 H21'), P('B_Green','P_Green','M3,17 H5.5 V14.5 H8 V17 H10.5 V19.5 H8 V22 H5.5 V19.5 H3 Z'))
G('IconUpdate', pg(), P('B_Green','P_Green','M12,8 C15,8 17,10 17,13 L18.5,13 L16,16.5 L13.5,13 L15,13 C15,11 13.8,10 12,10 Z M12,19 C9,19 7,17 7,14 L5.5,14 L8,10.5 L10.5,14 L9,14 C9,16 10.2,17 12,17 Z'))
G('IconStyleSet', R('#FFFFFFFF','P_Grey',2,3,13,16,1), R('#FFFFFFFF','P_Grey',9,6,13,16,1), P('#FF2B5797',None,'M12,18 L14.5,10 H16 L18.5,18 H17 L16.4,16 H14.1 L13.5,18 Z M14.5,14.8 H16 L15.25,12.4 Z'))
G('IconSpellGrammar', P('#FF2B5797',None,'M2,10 L4.5,3 H6 L8.5,10 H7 L6.4,8.3 H4.1 L3.5,10 Z M4.5,7 H6 L5.25,4.8 Z M9.5,3 H12 C14,3 14,6.3 12.3,6.4 C14.5,6.6 14.5,10 12.3,10 H9.5 Z M10.8,4.1 V5.9 H11.8 C12.8,5.9 12.8,4.1 11.8,4.1 Z M10.8,7 V8.9 H12 C13.1,8.9 13.1,7 12,7 Z M20,4 C19,3 16,2.6 15.6,6.5 C15.4,10.4 18.5,10.5 20,9.2'), P(None,'P_Check','M4,16 L8.5,20.5 L20.5,10.5'))

G('IconGrow', P('#FF2B5797',None,'M5,6 H8 L13.5,21 H10.6 L9.4,17.5 H3.6 L2.4,21 H-0.5 Z M4.4,15 H8.6 L6.5,8.8 Z'), P('B_Arrow','P_Arrow','M17.5,3 L22,9 H13 Z'))
G('IconShrink', P('#FF2B5797',None,'M5,9 H7.6 L12,21 H9.6 L8.7,18.3 H4 L3.1,21 H0.6 Z M4.7,16.2 H8 L6.35,11.4 Z'), P('B_Arrow','P_Arrow','M17.5,9 L22,3 H13 Z'))
G('IconChangeCase', P('#FF2B5797',None,'M5,4 H8 L13.5,20 H10.6 L9.4,16.5 H3.6 L2.4,20 H-0.5 Z M4.4,14 H8.6 L6.5,7.8 Z'), P('#FF2B5797',None,'M19,12 C21.5,12 22.5,13 22.5,15 V20 H20.6 V19 C19.8,19.9 19,20.2 18,20.2 C16.3,20.2 15,19.2 15,17.6 C15,15.6 16.8,15 20.5,14.8 C20.5,13.8 20,13.5 19,13.5 C18,13.5 17,13.8 16,14.4 L15.4,13 C16.6,12.3 17.8,12 19,12 Z M20.5,16.2 C18,16.3 17,16.7 17,17.5 C17,18.2 17.5,18.6 18.3,18.6 C19.3,18.6 20.5,18 20.5,16.9 Z'))

extra = '''
<LinearGradientBrush x:Key="B_WordArt" StartPoint="0,0" EndPoint="0,1"><GradientStop Color="#FFBDE2FF" Offset="0"/><GradientStop Color="#FF3B8FE0" Offset="0.5"/><GradientStop Color="#FF7A2BD8" Offset="1"/></LinearGradientBrush>
<SolidColorBrush x:Key="B_Wm" Color="#80A0A8B4"/>
<LinearGradientBrush x:Key="B_Gray" StartPoint="0,0" EndPoint="0,1"><GradientStop Color="#FFE0E0E0" Offset="0"/><GradientStop Color="#FF8A8A8A" Offset="1"/></LinearGradientBrush>
<Pen x:Key="P_Dots" Brush="#FF8C9BAD" Thickness="1" DashStyle="{x:Static DashStyles.Dot}"/>
<Pen x:Key="P_Strike" Brush="#FFD21E10" Thickness="1.2"/>
<Pen x:Key="P_Sun" Brush="#FFE09E00" Thickness="1.6" StartLineCap="Round" EndLineCap="Round"/>

<LinearGradientBrush x:Key="B_Fold" StartPoint="0,0" EndPoint="1,1"><GradientStop Color="#FFFFFFFF" Offset="0"/><GradientStop Color="#FFC9D3DF" Offset="1"/></LinearGradientBrush>
<LinearGradientBrush x:Key="B_Clip" StartPoint="0,0" EndPoint="0,1"><GradientStop Color="#FFE8C48A" Offset="0"/><GradientStop Color="#FFB98543" Offset="1"/></LinearGradientBrush>
<LinearGradientBrush x:Key="B_Frame" StartPoint="0,0" EndPoint="0,1"><GradientStop Color="#FFFFFFFF" Offset="0"/><GradientStop Color="#FFDDE3EA" Offset="1"/></LinearGradientBrush>
<LinearGradientBrush x:Key="B_Sky" StartPoint="0,0" EndPoint="0,1"><GradientStop Color="#FF8FD0FF" Offset="0"/><GradientStop Color="#FFDDF2FF" Offset="1"/></LinearGradientBrush>
<LinearGradientBrush x:Key="B_TblHead" StartPoint="0,0" EndPoint="0,1"><GradientStop Color="#FFB8D5F3" Offset="0"/><GradientStop Color="#FF77A7DC" Offset="1"/></LinearGradientBrush>
<LinearGradientBrush x:Key="B_Eraser" StartPoint="0,0" EndPoint="0,1"><GradientStop Color="#FFFFA9B6" Offset="0"/><GradientStop Color="#FFE5576E" Offset="1"/></LinearGradientBrush>
<Pen x:Key="P_Text" Brush="#FF8C9BAD" Thickness="1"/>
<Pen x:Key="P_Bar" Brush="#FF2E3D50" Thickness="1.6"/>
<Pen x:Key="P_Num" Brush="#FF1A57B5" Thickness="1.3" LineJoin="Round"/>
<Pen x:Key="P_Tbl" Brush="#FF3F6EA6" Thickness="0.9"/>
<Pen x:Key="P_ClipB" Brush="#FF7E5524" Thickness="0.9" LineJoin="Round"/>
<Pen x:Key="P_Scissor" Brush="#FFC02314" Thickness="2"/>
<Pen x:Key="P_Blade" Brush="#FF6A7480" Thickness="1.8" StartLineCap="Round" EndLineCap="Round"/>
<Pen x:Key="P_Chain" Brush="#FF4D6E93" Thickness="2.2" StartLineCap="Round" EndLineCap="Round"/>
<Pen x:Key="P_Ring" Brush="#FF59616B" Thickness="1.8" StartLineCap="Round" EndLineCap="Round"/>
<Pen x:Key="P_Dash" Brush="#FF2B79D8" Thickness="1.4" DashStyle="{x:Static DashStyles.Dash}"/>
<Pen x:Key="P_Dash2" Brush="#FF7DA2CE" Thickness="0.8" DashStyle="{x:Static DashStyles.Dash}"/>
<Pen x:Key="P_Rule" Brush="#FF4D6E93" Thickness="2"/>
<Pen x:Key="P_Plus" Brush="#FF1A57B5" Thickness="1.8"/>
'''
xaml = ('<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"\n'
        '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">\n' + BRUSHES + extra + '\n'.join(out) + '\n</ResourceDictionary>\n')
open(os.path.join(HERE, '..', 'src', 'ValcoWord', 'Themes', 'Icons.xaml'), 'w').write(xaml)
print(len(out), 'icons')
