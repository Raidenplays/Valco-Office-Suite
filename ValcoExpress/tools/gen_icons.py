# Generates Themes/Icons.xaml (vector DrawingImages, 24x24 unit grid)
out = []
def P(fill, pen, d):
    f = f' Brush="{{StaticResource {fill}}}"' if fill and fill.startswith('B_') else (f' Brush="{fill}"' if fill else '')
    p = f' Pen="{{StaticResource {pen}}}"' if pen else ''
    return f'<GeometryDrawing{f}{p} Geometry="{d}"/>'
def E(fill, pen, cx, cy, rx, ry=None):
    ry = ry or rx
    f = f' Brush="{{StaticResource {fill}}}"' if fill and fill.startswith('B_') else (f' Brush="{fill}"' if fill else '')
    p = f' Pen="{{StaticResource {pen}}}"' if pen else ''
    return f'<GeometryDrawing{f}{p}><GeometryDrawing.Geometry><EllipseGeometry Center="{cx},{cy}" RadiusX="{rx}" RadiusY="{ry}"/></GeometryDrawing.Geometry></GeometryDrawing>'
def R(fill, pen, x, y, w, h, r=0):
    f = f' Brush="{{StaticResource {fill}}}"' if fill and fill.startswith('B_') else (f' Brush="{fill}"' if fill else '')
    p = f' Pen="{{StaticResource {pen}}}"' if pen else ''
    return f'<GeometryDrawing{f}{p}><GeometryDrawing.Geometry><RectangleGeometry Rect="{x},{y},{w},{h}" RadiusX="{r}" RadiusY="{r}"/></GeometryDrawing.Geometry></GeometryDrawing>'
BOUNDS = '<GeometryDrawing Brush="#00FFFFFF" Geometry="M0,0 H24 V24 H0 Z"/>'
def G(key, *parts, image=True):
    body = BOUNDS + ''.join(parts)
    if image:
        out.append(f'<DrawingImage x:Key="{key}"><DrawingImage.Drawing><DrawingGroup>{body}</DrawingGroup></DrawingImage.Drawing></DrawingImage>')
def env(x, y, w, h):
    mx = x + w/2
    return (R('B_Env','P_Env',x,y,w,h,0.8) +
            P(None,'P_EnvThin',f'M{x+0.6},{y+h-0.6} L{mx-2.2},{y+h*0.45}') +
            P(None,'P_EnvThin',f'M{x+w-0.6},{y+h-0.6} L{mx+2.2},{y+h*0.45}') +
            P('B_EnvFlap','P_Env',f'M{x+0.4},{y+0.4} L{mx},{y+h*0.58} L{x+w-0.4},{y+0.4} Z'))
def folder(dx=0, dy=0):
    return (P('B_FolderBack','P_Folder',f'M{2.5+dx},{5.5+dy} H{9+dx} L{11+dx},{7.5+dy} H{21.5+dx} V{19.5+dy} H{2.5+dx} Z') +
            P('B_Folder','P_Folder',f'M{2.5+dx},{9.5+dy} H{21.5+dx} V{19.5+dy} H{2.5+dx} Z'))
star = 'M18,0.8 L19.5,4.2 L23.2,4.5 L20.4,6.9 L21.2,10.5 L18,8.6 L14.8,10.5 L15.6,6.9 L12.8,4.5 L16.5,4.2 Z'

G('IconEnvClosed', env(2.5,5.5,19,13))
G('IconEnvOpen',
  P('B_EnvFlapOpen','P_Env','M2.5,10 L12,3.5 L21.5,10 Z'),
  R('#FFFFFFFF','P_EnvThin',6,5.5,12,9),
  P('B_Env','P_Env','M2.5,10 H21.5 V19.5 H2.5 Z'),
  P(None,'P_EnvThin','M3,10.5 L12,16.5 L21,10.5'))
G('IconCreateMail', env(1.5,7.5,17,12), P('B_Gold','P_Gold',star))
G('IconReply', env(5.5,4.5,17,12),
  P('B_Arrow','P_Arrow','M0.8,16.5 L6.5,11.5 V14.2 C11,14 14,15.8 15.5,20.5 C12.5,18.2 10,17.6 6.5,17.6 V21.2 Z'))
G('IconReplyAll', env(6.5,3.5,16.5,11.5),
  P('B_Arrow','P_Arrow','M3.5,15 L9,10.5 V13 C13,13 15.5,14.6 17,18.6 C14.5,16.6 12.2,16.2 9,16.2 V19.5 Z'),
  P('B_Arrow','P_Arrow','M0.6,18 L6,13.5 V16 C10,16 12.5,17.6 14,21.8 C11.5,19.8 9.2,19.4 6,19.4 V22.6 Z'))
G('IconForward', env(1.5,4.5,17,12),
  P('B_Arrow','P_Arrow','M23.2,16.5 L17.5,11.5 V14.2 C13,14 10,15.8 8.5,20.5 C11.5,18.2 14,17.6 17.5,17.6 V21.2 Z'))
G('IconSendReceive', env(1.5,2.5,15,10.5),
  P('B_Green','P_Green','M8,12 H15 V9.5 L20.5,13.5 L15,17.5 V15 H8 Z'),
  P('B_Arrow','P_Arrow','M22,18.5 H15 V16 L9.5,20 L15,24 V21.5 H22 Z'))
G('IconSend', env(1.5,5.5,15,11),
  P('B_Green','P_Green','M10,14 H17 V11 L23,16 L17,21 V18 H10 Z'))
G('IconPrint',
  R('#FFFFFFFF','P_Grey',7,2.5,10,7),
  R('B_Grey','P_Grey',2.5,8.5,19,9,1.5),
  R('#FFFFFFFF','P_Grey',6,14.5,12,7),
  P(None,'P_EnvThin','M8,17 H16 M8,19 H14'),
  E('B_Green',None,18.8,11,1))
G('IconDelete', P('B_Red','P_Red','M4,6.5 L6.5,4 L12,9.5 L17.5,4 L20,6.5 L14.5,12 L20,17.5 L17.5,20 L12,14.5 L6.5,20 L4,17.5 L9.5,12 Z'))
G('IconContacts',
  P('B_Arrow','P_Arrow','M4,21.5 C4,15.5 7.5,13.2 12,13.2 C16.5,13.2 20,15.5 20,21.5 Z'),
  E('B_Skin','P_Skin',12,7.8,4.3))
G('IconFind', E('B_Lens','P_Lens',10,10,6.3), P(None,'P_Handle','M14.8,14.8 L21,21'))
G('IconFolder', folder())
G('IconFolders', folder())
G('IconSave', R('B_Arrow','P_Arrow',3,3,18,18,1.5), R('#FFFFFFFF','P_EnvThin',6.5,3.5,11,7), R('B_Grey','P_Grey',8,14,8,6.5))
G('IconAttach', P(None,'P_Clip','M15.5,6 V16.2 C15.5,20.2 8.5,20.2 8.5,16.2 V5.2 C8.5,2 13.2,2 13.2,5.2 V15.2 C13.2,17 10.8,17 10.8,15.2 V7.5'))
G('IconPriorityHigh', R('B_Red',None,10.3,2.5,3.4,12.5,1.6), E('B_Red',None,12,19.3,2))
G('IconPriorityLow', P('B_Arrow','P_Arrow','M12,21.5 L5.5,14 H9.8 V3 H14.2 V14 H18.5 Z'))
G('IconFlag', P(None,'P_Pole','M5,2.5 V22.5'),
  P('B_Red','P_Red','M5.6,3.5 C9.5,1.8 12.5,5.6 19.5,3.8 V13.2 C12.5,15 9.5,11.2 5.6,12.9 Z'))
G('IconSpelling', P(None,'P_Check','M3.5,12.5 L8.5,17.5 L20.5,4.5'), P(None,'P_Wavy','M3,21.5 Q5,19.5 7,21.5 T11,21.5 T15,21.5 T19,21.5'))
G('IconInbox',
  env(6.5,3,11,7.5),
  P('B_Tray','P_Tray','M2.5,12.5 L5.5,8.5 H18.5 L21.5,12.5 V20 H2.5 Z'),
  P('B_TrayIn','P_Tray','M2.5,12.5 H8 L9.5,15.2 H14.5 L16,12.5 H21.5 V20 H2.5 Z'))
G('IconOutbox',
  P('B_Tray','P_Tray','M2.5,12.5 L5.5,8.5 H18.5 L21.5,12.5 V20 H2.5 Z'),
  P('B_TrayIn','P_Tray','M2.5,12.5 H8 L9.5,15.2 H14.5 L16,12.5 H21.5 V20 H2.5 Z'),
  P('B_Green','P_Green','M12,1.2 L17.5,6.8 H14.2 V12 H9.8 V6.8 H6.5 Z'))
G('IconSent', env(1.5,3.5,16,11.5), P('B_Green','P_Green','M9,15 H16 V12.2 L22.5,17.2 L16,22.2 V19.4 H9 Z'))
G('IconDeleted',
  P('B_Grey','P_Grey','M5.5,7.5 L7,21.5 H17 L18.5,7.5 Z'),
  P(None,'P_GreyThin','M10,10 V19 M14,10 V19'),
  P('#FFFFFFFF','P_Grey','M9.5,4.5 V2.8 H14.5 V4.5'),
  R('B_Grey','P_Grey',3.8,4.5,16.4,3,1))
G('IconDrafts',
  P('#FFFFFFFF','P_Grey','M4.5,2.5 H14 L17.5,6 V21.5 H4.5 Z'),
  P(None,'P_GreyThin','M7,9 H15 M7,12 H15 M7,15 H12'),
  P('B_Gold','P_Gold','M20.6,7.2 L22.6,9.2 L13,18.8 L10.2,19.6 L11,16.8 Z'))
G('IconJunk', env(1.5,3.5,17,12), E('#FFFFFFFF','P_RedThick',17,17,5), P(None,'P_RedThick','M13.6,20.4 L20.4,13.6'))
G('IconLocalFolders', folder(0,0), env(9.5,12,11,7.5))
G('IconAccount',
  E('B_Globe','P_Globe',10.5,10.5,8.5),
  P('B_Land',None,'M5,6 C7,4 10,5 9.5,8 C9,10 6.5,9.5 6,12 C5.5,14 3.5,12 3,10 C2.8,8.5 3.8,7 5,6 Z M12,3.5 C14.5,3 17,5 16.5,7 C15.5,8.5 13,7.5 12.5,9.5 C12,11 14,12 15.5,12.5 C17,13 17.5,15 16,16.5 C14.5,17.5 13,16 12.5,14.5 C12,13 10.5,12.5 10.5,10.5 C10.5,8.5 11,5 12,3.5 Z'),
  env(11.5,13,11.5,8.5))
G('IconInfo', E('B_Arrow','P_Arrow',12,12,10.5), E('#FFFFFFFF',None,12,6.8,1.7), R('#FFFFFFFF',None,10.4,9.8,3.2,9,0.8))
G('IconQuestion', E('B_Arrow','P_Arrow',12,12,10.5), P(None,'P_WhiteFat','M8.8,9.2 C8.8,5.4 15.2,5.4 15.2,9.2 C15.2,12 12,11.8 12,14.8'), E('#FFFFFFFF',None,12,18.2,1.6))
G('IconWarning', P('B_Gold','P_Gold','M12,1.8 L23,21.6 H1 Z'), R('#FF000000',None,10.9,8,2.2,7.5,1), E('#FF000000',None,12,18.3,1.4))
G('IconError', E('B_Red','P_Red',12,12,10.5), P(None,'P_WhiteFat','M8,8 L16,16 M16,8 L8,16'))
G('IconPrevious', P('B_Arrow','P_Arrow','M12,2.5 L20.5,12 H15.2 V21.5 H8.8 V12 H3.5 Z'))
G('IconNext', P('B_Arrow','P_Arrow','M12,21.5 L20.5,12 H15.2 V2.5 H8.8 V12 H3.5 Z'))
G('IconAddContact',
  P('B_Arrow','P_Arrow','M2.5,21.5 C2.5,16 5.5,13.5 10,13.5 C14.5,13.5 17.5,16 17.5,21.5 Z'),
  E('B_Skin','P_Skin',10,7.8,4.2),
  P('B_Green','P_Green','M17.8,1.5 H20.6 V4.6 H23.6 V7.4 H20.6 V10.5 H17.8 V7.4 H14.8 V4.6 H17.8 Z'))
G('IconProperties', R('#FFFFFFFF','P_Grey',3.5,2.5,17,19,1), P(None,'P_GreyThin','M7,7 H17 M7,10.5 H17 M7,14 H17 M7,17.5 H13'), E('B_Arrow','P_Arrow',18,18,4.5), R('#FFFFFFFF',None,17.3,16.6,1.4,3.8))
G('IconApp',
  E('B_AppDisc','P_AppDisc',12,12,11),
  P('B_AppShine',None,'M3.2,10 C3.5,5 7.5,1.8 12,1.8 C16.5,1.8 20.5,5 20.8,10 C17,8.2 7,8.2 3.2,10 Z'),
  env(5,7.5,14,10))

brushes = '''
<LinearGradientBrush x:Key="B_Env" StartPoint="0,0" EndPoint="0,1"><GradientStop Color="#FFFFFFFF" Offset="0"/><GradientStop Color="#FFD3E0F0" Offset="1"/></LinearGradientBrush>
<LinearGradientBrush x:Key="B_EnvFlap" StartPoint="0,0" EndPoint="0,1"><GradientStop Color="#FFFFFFFF" Offset="0"/><GradientStop Color="#FFE3EBF5" Offset="1"/></LinearGradientBrush>
<LinearGradientBrush x:Key="B_EnvFlapOpen" StartPoint="0,0" EndPoint="0,1"><GradientStop Color="#FFDDE7F3" Offset="0"/><GradientStop Color="#FFB9CCE3" Offset="1"/></LinearGradientBrush>
<LinearGradientBrush x:Key="B_Arrow" StartPoint="0,0" EndPoint="0,1"><GradientStop Color="#FF92CEFF" Offset="0"/><GradientStop Color="#FF2B79D8" Offset="0.55"/><GradientStop Color="#FF1A57B5" Offset="1"/></LinearGradientBrush>
<LinearGradientBrush x:Key="B_Green" StartPoint="0,0" EndPoint="0,1"><GradientStop Color="#FFC2F59B" Offset="0"/><GradientStop Color="#FF46B62C" Offset="0.55"/><GradientStop Color="#FF2A8A18" Offset="1"/></LinearGradientBrush>
<LinearGradientBrush x:Key="B_Red" StartPoint="0,0" EndPoint="0,1"><GradientStop Color="#FFFFA594" Offset="0"/><GradientStop Color="#FFE5331E" Offset="0.55"/><GradientStop Color="#FFB4150A" Offset="1"/></LinearGradientBrush>
<LinearGradientBrush x:Key="B_Gold" StartPoint="0,0" EndPoint="0,1"><GradientStop Color="#FFFFF2A8" Offset="0"/><GradientStop Color="#FFF7C534" Offset="0.55"/><GradientStop Color="#FFE09E00" Offset="1"/></LinearGradientBrush>
<LinearGradientBrush x:Key="B_Folder" StartPoint="0,0" EndPoint="0,1"><GradientStop Color="#FFFFF0BC" Offset="0"/><GradientStop Color="#FFF5CB62" Offset="1"/></LinearGradientBrush>
<LinearGradientBrush x:Key="B_FolderBack" StartPoint="0,0" EndPoint="0,1"><GradientStop Color="#FFF3D27A" Offset="0"/><GradientStop Color="#FFDDA83A" Offset="1"/></LinearGradientBrush>
<LinearGradientBrush x:Key="B_Grey" StartPoint="0,0" EndPoint="0,1"><GradientStop Color="#FFFFFFFF" Offset="0"/><GradientStop Color="#FFB9C1CA" Offset="1"/></LinearGradientBrush>
<LinearGradientBrush x:Key="B_Tray" StartPoint="0,0" EndPoint="0,1"><GradientStop Color="#FFB8D2EE" Offset="0"/><GradientStop Color="#FF5E8DC2" Offset="1"/></LinearGradientBrush>
<LinearGradientBrush x:Key="B_TrayIn" StartPoint="0,0" EndPoint="0,1"><GradientStop Color="#FFE3EFFB" Offset="0"/><GradientStop Color="#FF8DB2DA" Offset="1"/></LinearGradientBrush>
<LinearGradientBrush x:Key="B_Skin" StartPoint="0,0" EndPoint="0,1"><GradientStop Color="#FFFFE6CF" Offset="0"/><GradientStop Color="#FFEDB98E" Offset="1"/></LinearGradientBrush>
<RadialGradientBrush x:Key="B_Lens" GradientOrigin="0.35,0.3"><GradientStop Color="#EEFFFFFF" Offset="0"/><GradientStop Color="#AA9FD4F5" Offset="1"/></RadialGradientBrush>
<RadialGradientBrush x:Key="B_Globe" GradientOrigin="0.35,0.3"><GradientStop Color="#FFBDEBFF" Offset="0"/><GradientStop Color="#FF3E9BE0" Offset="0.6"/><GradientStop Color="#FF1A5FAE" Offset="1"/></RadialGradientBrush>
<LinearGradientBrush x:Key="B_Land" StartPoint="0,0" EndPoint="0,1"><GradientStop Color="#FFA6E37A" Offset="0"/><GradientStop Color="#FF3E9A2A" Offset="1"/></LinearGradientBrush>
<RadialGradientBrush x:Key="B_AppDisc" GradientOrigin="0.5,0.85" Center="0.5,0.7" RadiusX="0.75" RadiusY="0.75"><GradientStop Color="#FF7FE0FF" Offset="0"/><GradientStop Color="#FF2C8AD6" Offset="0.55"/><GradientStop Color="#FF0F4C99" Offset="1"/></RadialGradientBrush>
<LinearGradientBrush x:Key="B_AppShine" StartPoint="0,0" EndPoint="0,1"><GradientStop Color="#C0FFFFFF" Offset="0"/><GradientStop Color="#20FFFFFF" Offset="1"/></LinearGradientBrush>
<Pen x:Key="P_Env" Brush="#FF4B72A0" Thickness="0.9" LineJoin="Round"/>
<Pen x:Key="P_EnvThin" Brush="#FF8DA8C8" Thickness="0.7" LineJoin="Round"/>
<Pen x:Key="P_Arrow" Brush="#FF124790" Thickness="0.8" LineJoin="Round"/>
<Pen x:Key="P_Green" Brush="#FF1D6A10" Thickness="0.8" LineJoin="Round"/>
<Pen x:Key="P_Red" Brush="#FF84100A" Thickness="0.8" LineJoin="Round"/>
<Pen x:Key="P_RedThick" Brush="#FFD21E10" Thickness="2" StartLineCap="Round" EndLineCap="Round"/>
<Pen x:Key="P_Gold" Brush="#FF9A6A00" Thickness="0.8" LineJoin="Round"/>
<Pen x:Key="P_Folder" Brush="#FFAF801C" Thickness="0.8" LineJoin="Round"/>
<Pen x:Key="P_Grey" Brush="#FF66707B" Thickness="0.9" LineJoin="Round"/>
<Pen x:Key="P_GreyThin" Brush="#FF8C959F" Thickness="0.8"/>
<Pen x:Key="P_Tray" Brush="#FF2F5D92" Thickness="0.8" LineJoin="Round"/>
<Pen x:Key="P_Skin" Brush="#FFA87450" Thickness="0.8"/>
<Pen x:Key="P_Lens" Brush="#FF3E5C7C" Thickness="2"/>
<Pen x:Key="P_Handle" Brush="#FF5A4127" Thickness="3.2" StartLineCap="Round" EndLineCap="Round"/>
<Pen x:Key="P_Clip" Brush="#FF5E6873" Thickness="1.7" StartLineCap="Round" EndLineCap="Round" LineJoin="Round"/>
<Pen x:Key="P_Pole" Brush="#FF59616B" Thickness="1.6" StartLineCap="Round" EndLineCap="Round"/>
<Pen x:Key="P_Check" Brush="#FF2E9A1C" Thickness="3" StartLineCap="Round" EndLineCap="Round" LineJoin="Round"/>
<Pen x:Key="P_Wavy" Brush="#FFD21E10" Thickness="1.4"/>
<Pen x:Key="P_WhiteFat" Brush="#FFFFFFFF" Thickness="2.6" StartLineCap="Round" EndLineCap="Round"/>
<Pen x:Key="P_Globe" Brush="#FF155896" Thickness="0.9"/>
<Pen x:Key="P_AppDisc" Brush="#FF0B3A78" Thickness="0.9"/>
'''
xaml = ('<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"\n'
        '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">\n' + brushes + '\n'.join(out) + '\n</ResourceDictionary>\n')
open('/home/claude/ValcoExpress/src/AeroMail/Themes/Icons.xaml','w').write(xaml)
print(len(out), 'icons')
