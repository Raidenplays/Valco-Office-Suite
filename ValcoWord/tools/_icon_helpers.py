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
BRUSHES = '''
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
