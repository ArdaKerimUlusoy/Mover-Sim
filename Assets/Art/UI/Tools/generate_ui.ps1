# Sahte Şaheser - UI varlık üreticisi (PowerShell 5.1 + System.Drawing).
# Yeniden üretilebilir: aynı tohumla her çalıştırmada aynı PNG'ler çıkar.
# Kullanım:  powershell -ExecutionPolicy Bypass -File Assets\Art\UI\Tools\generate_ui.ps1 [-OutRoot <klasör>]
# Varsayılan çıktı: Assets/Resources/UI/{Panels,Buttons,Frames,Icons,Backgrounds,Textures}
# (OnGUI kodu Resources.Load ile yükler; tek kaynak burasıdır - Art/UI'da kopya tutulmaz.)
# Renkler UI_DESIGN.md token tablosu ve UITheme.cs ile birebir aynıdır. Yazı içeren doku YOKTUR.
param([string]$OutRoot = "")

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if ([string]::IsNullOrEmpty($OutRoot)) {
    $OutRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\Resources\UI'))
}
foreach ($d in 'Panels','Buttons','Frames','Icons','Backgrounds','Textures') {
    New-Item -ItemType Directory -Force -Path (Join-Path $OutRoot $d) | Out-Null
}
$script:count = 0
$script:rng = New-Object System.Random 1907   # sabit tohum

# ---------------- Token'lar (UITheme.cs ile aynı) ----------------
function HexC([string]$hex, [int]$a = 255) {
    $h = $hex.TrimStart('#')
    return [System.Drawing.Color]::FromArgb($a, [Convert]::ToInt32($h.Substring(0,2),16), [Convert]::ToInt32($h.Substring(2,2),16), [Convert]::ToInt32($h.Substring(4,2),16))
}
$Ivory      = '#F2EAD8'
$IvoryDim   = '#D9CDB4'
$Paper      = '#EFE6D2'
$Charcoal   = '#1E1B19'
$CharSoft   = '#2C2724'
$CharDeep   = '#141210'
$Graphite   = '#4A443F'
$Bordeaux   = '#7A1F2B'
$BordeauxHi = '#8E2633'
$BordeauxLo = '#5E1721'
$Forest     = '#1F3A2E'
$Brass      = '#B08D57'
$BrassLight = '#D8B97A'
$BrassDark  = '#7A6038'
$Terracotta = '#B8664A'
$Danger     = '#C2483D'

# ---------------- Yardımcılar ----------------
function New-Bmp([int]$w, [int]$h) {
    $b = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($b)
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.Dispose()
    return $b
}
function Save-Png($bmp, [string]$folder, [string]$name) {
    $p = Join-Path (Join-Path $OutRoot $folder) ($name + '.png')
    $bmp.Save($p, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $script:count++
}
# Keskin (yumuşatmasız) dolu dikdörtgen
function Box($bmp, [int]$x, [int]$y, [int]$w, [int]$h, $col, [switch]$Copy) {
    if ($w -le 0 -or $h -le 0) { return }
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'None'
    if ($Copy) { $g.CompositingMode = 'SourceCopy' }
    $br = New-Object System.Drawing.SolidBrush $col
    $g.FillRectangle($br, $x, $y, $w, $h)
    $br.Dispose(); $g.Dispose()
}
# t kalınlıklı keskin çerçeve, i: içeri girinti
function Outline($bmp, [int]$i, [int]$t, $col) {
    $w = $bmp.Width; $h = $bmp.Height
    Box $bmp $i $i ($w - 2*$i) $t $col
    Box $bmp $i ($h - $i - $t) ($w - 2*$i) $t $col
    Box $bmp $i ($i + $t) $t ($h - 2*$i - 2*$t) $col
    Box $bmp ($w - $i - $t) ($i + $t) $t ($h - 2*$i - 2*$t) $col
}
# Köşeleri c piksel pahla (saydam)
function Chamfer($bmp, [int]$c) {
    $w = $bmp.Width; $h = $bmp.Height; $clr = [System.Drawing.Color]::Transparent
    for ($k = 0; $k -lt $c; $k++) {
        for ($j = 0; $j -lt ($c - $k); $j++) {
            $bmp.SetPixel($k, $j, $clr); $bmp.SetPixel($w-1-$k, $j, $clr)
            $bmp.SetPixel($k, $h-1-$j, $clr); $bmp.SetPixel($w-1-$k, $h-1-$j, $clr)
        }
    }
}
# Arşiv fotoğraf köşeleri (dört köşede dik üçgen)
function PhotoCorners($bmp, [int]$inset, [int]$size, $col) {
    $w = $bmp.Width; $h = $bmp.Height
    for ($y = 0; $y -lt $size; $y++) {
        for ($x = 0; $x -lt ($size - $y); $x++) {
            $bmp.SetPixel($inset + $x, $inset + $y, $col)
            $bmp.SetPixel($w-1-$inset-$x, $inset + $y, $col)
            $bmp.SetPixel($inset + $x, $h-1-$inset-$y, $col)
            $bmp.SetPixel($w-1-$inset-$x, $h-1-$inset-$y, $col)
        }
    }
}
function Smooth([double]$a, [double]$b, [double]$x) {
    $t = [Math]::Min(1.0, [Math]::Max(0.0, ($x - $a) / ($b - $a)))
    return $t * $t * (3 - 2 * $t)
}

# ================= PANELS =================
# Plaket (oyun içi koyu panel): kömür zemin, 1 px koyu dış hat, 4 px içeride ince pirinç çizgi. Kenar 12.
$b = New-Bmp 48 48
Box $b 0 0 48 48 (HexC $Charcoal 238)
Outline $b 0 1 (HexC '#0B0A09' 255)
Outline $b 4 1 (HexC $Brass 190)
Chamfer $b 2
Save-Png $b 'Panels' 'panel_plaque'

# Küçük plaket (HUD satırları, < 32 px kutular). Kenar 6.
$b = New-Bmp 24 24
Box $b 0 0 24 24 (HexC $Charcoal 232)
Outline $b 0 1 (HexC '#0B0A09' 255)
Outline $b 2 1 (HexC $Brass 120)
Chamfer $b 1
Save-Png $b 'Panels' 'panel_plaque_small'

# Katalog sayfası (açık, belge): fildişi, grafit dış hat, 6 px içeride çift çizgi. Kenar 16.
$b = New-Bmp 64 64
Box $b 0 0 64 64 (HexC $Ivory 252)
Outline $b 0 1 (HexC $Graphite 255)
Outline $b 6 1 (HexC $Graphite 70)
Save-Png $b 'Panels' 'panel_catalog'

# Eskiz kâğıdı: kâğıt rengi, ince grafit hat, solda terrakota kenar boşluğu çizgisi, sağ üstte kıvrık köşe. Kenar 24.
$b = New-Bmp 96 96
Box $b 0 0 96 96 (HexC $Paper 252)
Outline $b 0 1 (HexC $Graphite 150)
Box $b 18 1 1 94 (HexC $Terracotta 120)
# kıvrık köşe (sağ üst, 14 px)
for ($y = 0; $y -lt 14; $y++) { for ($x = 0; $x -lt 14; $x++) {
    $s = $x + $y   # $x: sağ kenardan uzaklık
    if ($s -lt 13) { $b.SetPixel(95 - $x, $y, [System.Drawing.Color]::Transparent) }
    elseif ($s -eq 13 -or $x -eq 13 -or $y -eq 13) { $b.SetPixel(95 - $x, $y, (HexC $Graphite 160)) }
    else { $b.SetPixel(95 - $x, $y, (HexC $IvoryDim 255)) }
}}
Save-Png $b 'Panels' 'panel_sketch'

# Sergi etiketi: fildişi kart, kömür 1 px kenar, altta ince koyu çizgi. Kenar 8.
$b = New-Bmp 32 32
Box $b 0 0 32 32 (HexC '#F7F1E3' 255)
Outline $b 0 1 (HexC $Charcoal 230)
Box $b 1 29 30 2 (HexC $IvoryDim 255)
Save-Png $b 'Panels' 'panel_label'

# Yumuşak gölge (yalnızca Dialog altında). Kenar 16.
$b = New-Bmp 48 48
for ($y = 0; $y -lt 48; $y++) { for ($x = 0; $x -lt 48; $x++) {
    $dx = [Math]::Max(0, [Math]::Max(16 - $x, $x - 31)); $dy = [Math]::Max(0, [Math]::Max(16 - $y, $y - 31))
    $d = [Math]::Sqrt($dx*$dx + $dy*$dy)
    $a = [int](90 * (1 - (Smooth 0 16 $d)))
    if ($a -gt 0) { $b.SetPixel($x, $y, [System.Drawing.Color]::FromArgb($a, 8, 6, 5)) }
}}
Save-Png $b 'Panels' 'panel_shadow'

# Yazı alanı: derin kömür, grafit hat, altta 2 px pirinç. Odak: tüm hat pirinç, alt açık pirinç. Kenar 8.
$b = New-Bmp 32 32
Box $b 0 0 32 32 (HexC $CharDeep 242)
Outline $b 0 1 (HexC $Graphite 255)
Box $b 1 29 30 2 (HexC $Brass 255)
Save-Png $b 'Panels' 'field'
$b = New-Bmp 32 32
Box $b 0 0 32 32 (HexC $CharDeep 250)
Outline $b 0 1 (HexC $Brass 255)
Box $b 1 29 30 2 (HexC $BrassLight 255)
Save-Png $b 'Panels' 'field_focus'

# Yuva (envanter): yumuşak kömür + arşiv fotoğraf köşeleri. Kenar 12.
$b = New-Bmp 48 48
Box $b 0 0 48 48 (HexC $CharSoft 215)
Outline $b 0 1 (HexC $Graphite 230)
PhotoCorners $b 3 6 (HexC $Brass 130)
Save-Png $b 'Panels' 'slot'
$b = New-Bmp 48 48
Box $b 0 0 48 48 (HexC $CharSoft 225)
Outline $b 0 1 (HexC $Brass 200)
PhotoCorners $b 3 7 (HexC $Brass 210)
Save-Png $b 'Panels' 'slot_hover'
$b = New-Bmp 48 48
Box $b 0 0 48 48 (HexC $CharSoft 238)
Outline $b 0 2 (HexC $BrassLight 255)
PhotoCorners $b 4 8 (HexC $BrassLight 255)
Save-Png $b 'Panels' 'slot_selected'

# İlerleme çubuğu: oluk + (koddan renklendirilen) beyaz dolgu. Kenar 4 / 3.
$b = New-Bmp 24 12
Box $b 0 0 24 12 (HexC $CharDeep 230)
Outline $b 0 1 (HexC $Graphite 255)
Save-Png $b 'Panels' 'prog_track'
$b = New-Bmp 24 12
Box $b 0 0 24 12 ([System.Drawing.Color]::FromArgb(255, 255, 255, 255))
Box $b 0 10 24 2 ([System.Drawing.Color]::FromArgb(255, 200, 200, 200)) -Copy
Box $b 0 0 24 1 ([System.Drawing.Color]::FromArgb(255, 235, 235, 235)) -Copy
Save-Png $b 'Panels' 'prog_fill'

# Tuş kapağı: fildişi, kömür hat, altta 3 px dudak. Kenar (7,7,6,9).
$b = New-Bmp 24 26
Box $b 0 0 24 26 (HexC $Ivory 255)
Box $b 1 21 22 4 (HexC '#B9AC92' 255)
Outline $b 0 1 (HexC $Charcoal 255)
Chamfer $b 2
Save-Png $b 'Panels' 'keycap'

# Rozet: envanter etiketi biçimi (solda sivri uç + delik). Beyaz, koddan renklendirilir. Kenar (8,4,4,4).
$b = New-Bmp 24 16
for ($y = 0; $y -lt 16; $y++) { for ($x = 0; $x -lt 24; $x++) {
    $cut = [Math]::Abs($y - 7.5) - 3.5   # sol uçta pah
    if ($x -ge $cut) { $b.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(255, 255, 255, 255)) }
}}
foreach ($pt in @(@(5,7),@(6,7),@(5,8),@(6,8))) { $b.SetPixel($pt[0], $pt[1], [System.Drawing.Color]::Transparent) }
Save-Png $b 'Panels' 'badge'

# ================= FRAMES =================
# İnce pirinç çerçeve (görüntü / yayın / kat planı). Ortası saydam. Kenar 6.
$b = New-Bmp 32 32
Outline $b 0 1 (HexC '#2A2016' 255)
Outline $b 1 1 (HexC $BrassLight 255)
Outline $b 2 1 (HexC $Brass 255)
Outline $b 3 1 (HexC $BrassDark 255)
Outline $b 4 1 ([System.Drawing.Color]::FromArgb(110, 0, 0, 0))
Save-Png $b 'Frames' 'frame_brass'
# Klavye odağı: 2 px açık pirinç hat. Kenar 5.
$b = New-Bmp 16 16
Outline $b 0 2 (HexC $BrassLight 255)
Chamfer $b 1
Save-Png $b 'Frames' 'frame_focus'
# Arşiv köşeleri (yalnızca dört köşe, pirinç). Kenar 10.
$b = New-Bmp 32 32
PhotoCorners $b 0 9 (HexC $Brass 235)
PhotoCorners $b 0 2 (HexC $BrassLight 255)
Save-Png $b 'Frames' 'frame_corners'

# ================= BUTTONS =================
# 40x32, kenar 10. fill / üst ışık / alt dudak / dış hat / iç vurgu hattı (hover'da pirinç)
function New-Btn([string]$name, $fill, $hi, $lip, $outer, $inner, [int]$lipH = 2, [switch]$Pressed) {
    $b = New-Bmp 40 32
    Box $b 0 0 40 32 $fill
    if ($Pressed) { Box $b 1 1 38 2 $lip } else { Box $b 1 1 38 1 $hi; Box $b 1 (31 - $lipH) 38 $lipH $lip }
    if ($inner -ne $null) { Outline $b 2 1 $inner }
    Outline $b 0 1 $outer
    Chamfer $b 1
    Save-Png $b 'Buttons' $name
}
# Birincil: bordo
New-Btn 'btn_primary_normal'   (HexC $Bordeaux) (HexC '#9A3A45') (HexC '#4A1219') (HexC '#2A0A0F') $null
New-Btn 'btn_primary_hover'    (HexC $BordeauxHi) (HexC '#A84552') (HexC '#4A1219') (HexC '#2A0A0F') (HexC $BrassLight 200)
New-Btn 'btn_primary_pressed'  (HexC $BordeauxLo) (HexC $BordeauxLo) (HexC '#3A0E15') (HexC '#2A0A0F') (HexC $Brass 160) -Pressed
New-Btn 'btn_primary_disabled' (HexC $Graphite 190) (HexC $Graphite 190) (HexC '#36312D' 190) (HexC '#26221F' 200) $null
# İkincil: fildişi kâğıt sekmesi (kömür yazı)
New-Btn 'btn_secondary_normal'   (HexC $IvoryDim) (HexC '#EDE4D0') (HexC '#B9AC92') (HexC $CharSoft) $null
New-Btn 'btn_secondary_hover'    (HexC $Ivory) (HexC '#FBF6EA') (HexC '#C7BA9F') (HexC $CharSoft) (HexC $Brass 230)
New-Btn 'btn_secondary_pressed'  (HexC '#CFC2A8') (HexC '#CFC2A8') (HexC '#A89B82') (HexC $CharSoft) (HexC $Brass 200) -Pressed
New-Btn 'btn_secondary_disabled' (HexC '#8C857A' 150) (HexC '#8C857A' 150) (HexC '#6E685F' 150) (HexC '#4A443F' 160) $null
# Tehlike: kömür zemin + kırmızı iç hat; hover'da kırmızı dolgu
New-Btn 'btn_danger_normal'   (HexC $CharSoft 245) (HexC '#3A3430') (HexC '#151311') (HexC '#0B0A09') (HexC $Danger 255)
New-Btn 'btn_danger_hover'    (HexC '#A63A30') (HexC '#B9493E') (HexC '#6E241D') (HexC '#2A0A0F') (HexC '#E07A6E' 200)
New-Btn 'btn_danger_pressed'  (HexC '#7E2A22') (HexC '#7E2A22') (HexC '#5A1C16') (HexC '#2A0A0F') (HexC $Danger 200) -Pressed
New-Btn 'btn_danger_disabled' (HexC $Graphite 170) (HexC $Graphite 170) (HexC '#36312D' 170) (HexC '#26221F' 190) $null

# Araç düğmesi (ikonlu kare, palet / tezgah araçları). 40x40, kenar 10.
function New-Tool([string]$name, $fill, $line, $outer) {
    $b = New-Bmp 40 40
    Box $b 0 0 40 40 $fill
    Outline $b 0 1 $outer
    Outline $b 2 1 $line
    Chamfer $b 1
    Save-Png $b 'Buttons' $name
}
New-Tool 'btn_tool_normal'   (HexC $CharSoft 240) (HexC $Brass 110) (HexC '#0B0A09')
New-Tool 'btn_tool_hover'    (HexC '#36302C' 245) (HexC $BrassLight 230) (HexC '#0B0A09')
New-Tool 'btn_tool_pressed'  (HexC '#1A1715' 250) (HexC $Brass 200) (HexC '#0B0A09')
New-Tool 'btn_tool_selected' (HexC $Brass 255) (HexC $BrassLight 255) (HexC '#2A2016')
New-Tool 'btn_tool_disabled' (HexC $CharSoft 120) (HexC $Graphite 120) (HexC '#0B0A09' 120)

# Kaydırıcı (GUI.skin.horizontalSlider): 16x12 oluk (ortada 2 px çizgi), 10x12 başlık
$b = New-Bmp 16 12
Box $b 0 5 16 2 (HexC $BrassDark 255)
Box $b 0 4 1 4 (HexC $BrassDark 255); Box $b 15 4 1 4 (HexC $BrassDark 255)
Save-Png $b 'Buttons' 'slider_track'
foreach ($st in @(@('slider_thumb', $Brass), @('slider_thumb_hover', $BrassLight))) {
    $b = New-Bmp 10 12
    Box $b 1 0 8 12 (HexC $st[1])
    Box $b 0 1 10 10 (HexC $st[1])
    Box $b 4 4 2 4 (HexC $Charcoal 220)
    Outline $b 0 1 (HexC '#2A2016' 200)
    Save-Png $b 'Buttons' $st[0]
}
# Onay kutusu (GUI.skin.toggle): 16x16
$b = New-Bmp 16 16
Box $b 0 0 16 16 (HexC $CharDeep 230)
Outline $b 0 1 (HexC $Brass 230)
Save-Png $b 'Buttons' 'toggle_off'
$b = New-Bmp 16 16
Box $b 0 0 16 16 (HexC $Brass 255)
Outline $b 0 1 (HexC $BrassLight 255)
$g = [System.Drawing.Graphics]::FromImage($b); $g.SmoothingMode = 'AntiAlias'
$pen = New-Object System.Drawing.Pen ((HexC $Charcoal 255)), 2.2
$pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
$g.DrawLines($pen, [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF 3.5,8.5),(New-Object System.Drawing.PointF 6.5,11.5),(New-Object System.Drawing.PointF 12.5,4.5)))
$pen.Dispose(); $g.Dispose()
Save-Png $b 'Buttons' 'toggle_on'

# ================= BACKGROUNDS =================
# Kenar vinyeti (menü / duraklat: oyunun gerçek sahnesinin üstüne)
$b = New-Bmp 256 256
for ($y = 0; $y -lt 256; $y++) { for ($x = 0; $x -lt 256; $x++) {
    $nx = ($x + 0.5 - 128) / 128.0; $ny = ($y + 0.5 - 128) / 128.0
    $d = [Math]::Sqrt($nx*$nx*0.85 + $ny*$ny)
    $a = [int](205 * (Smooth 0.45 1.25 $d))
    if ($a -gt 0) { $b.SetPixel($x, $y, [System.Drawing.Color]::FromArgb($a, 20, 18, 16)) }
}}
Save-Png $b 'Backgrounds' 'bg_vignette'
# Soldan sağa kömür perde (ana menü sütununun okunurluğu)
$b = New-Bmp 256 4
for ($x = 0; $x -lt 256; $x++) {
    $a = [int](238 * (1 - (Smooth 0.0 1.0 ($x / 255.0))))
    for ($y = 0; $y -lt 4; $y++) { $b.SetPixel($x, $y, [System.Drawing.Color]::FromArgb($a, 20, 18, 16)) }
}
Save-Png $b 'Backgrounds' 'bg_scrim_left'
# Aşağıdan yukarı perde (HUD alt şeridi, görev sonu)
$b = New-Bmp 4 256
for ($y = 0; $y -lt 256; $y++) {
    $a = [int](200 * (Smooth 0.0 1.0 ($y / 255.0)))
    for ($x = 0; $x -lt 4; $x++) { $b.SetPixel($x, $y, [System.Drawing.Color]::FromArgb($a, 20, 18, 16)) }
}
Save-Png $b 'Backgrounds' 'bg_scrim_bottom'

# ================= TEXTURES (döşenir; yalnızca eskiz kâğıdı ve özel durumlar) =================
$b = New-Bmp 128 128
for ($y = 0; $y -lt 128; $y++) { for ($x = 0; $x -lt 128; $x++) {
    $v = $script:rng.Next(0, 100)
    if ($v -lt 22) { $b.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(10 + $script:rng.Next(0, 12), 60, 50, 40)) }
    elseif ($v -gt 92) { $b.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(16, 255, 255, 255)) }
}}
Save-Png $b 'Textures' 'tex_paper_grain'
$b = New-Bmp 32 32
Box $b 0 0 32 1 (HexC $Graphite 34)
Box $b 0 1 1 31 (HexC $Graphite 34)
Box $b 16 1 1 31 (HexC $Graphite 14); Box $b 1 16 31 1 (HexC $Graphite 14)
Save-Png $b 'Textures' 'tex_sketch_grid'
$b = New-Bmp 16 16
for ($y = 0; $y -lt 16; $y++) { for ($x = 0; $x -lt 16; $x++) {
    $m = ($x + $y) % 8
    if ($m -lt 2) { $b.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(255, 255, 255, 255)) }
}}
Save-Png $b 'Textures' 'tex_hatch'
# Bölüm çizgisi süsü (pirinç baklava)
$b = New-Bmp 12 12
$g = [System.Drawing.Graphics]::FromImage($b); $g.SmoothingMode = 'AntiAlias'
$pts = [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF 6,1),(New-Object System.Drawing.PointF 11,6),(New-Object System.Drawing.PointF 6,11),(New-Object System.Drawing.PointF 1,6))
$br = New-Object System.Drawing.SolidBrush (HexC $Brass 255); $g.FillPolygon($br, $pts); $br.Dispose()
$br = New-Object System.Drawing.SolidBrush (HexC $BrassLight 255)
$g.FillPolygon($br, [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF 6,3.5),(New-Object System.Drawing.PointF 8.5,6),(New-Object System.Drawing.PointF 6,8.5),(New-Object System.Drawing.PointF 3.5,6))); $br.Dispose()
$g.Dispose()
Save-Png $b 'Textures' 'ornament_diamond'

# ================= ICONS =================
# 64x64, saydam zemin, BEYAZ 4 px çizgi (yuvarlak uç / birleşim), canlı alan 6..58. Kodda renklendirilir.
function P([double]$x, [double]$y) { return New-Object System.Drawing.PointF ([single]$x), ([single]$y) }
function Icon([string]$name, [scriptblock]$draw) {
    $b = New-Bmp 64 64
    $g = [System.Drawing.Graphics]::FromImage($b)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), 4
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
    $fill = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $erase = New-Object System.Drawing.Pen ([System.Drawing.Color]::Transparent), 11
    $erase.StartCap = 'Round'; $erase.EndCap = 'Round'
    & $draw $g $pen $fill $erase
    $pen.Dispose(); $fill.Dispose(); $erase.Dispose(); $g.Dispose()
    Save-Png $b 'Icons' ('icon_' + $name)
}
function Lines($g, $pen, [double[]]$xy) {
    $pts = New-Object 'System.Collections.Generic.List[System.Drawing.PointF]'
    for ($i = 0; $i -lt $xy.Length; $i += 2) { $pts.Add((P $xy[$i] $xy[$i+1])) }
    $g.DrawLines($pen, $pts.ToArray())
}
function RRect($g, $pen, [double]$x, [double]$y, [double]$w, [double]$h, [double]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc([single]$x, [single]$y, [single]$d, [single]$d, 180, 90)
    $p.AddArc([single]($x + $w - $d), [single]$y, [single]$d, [single]$d, 270, 90)
    $p.AddArc([single]($x + $w - $d), [single]($y + $h - $d), [single]$d, [single]$d, 0, 90)
    $p.AddArc([single]$x, [single]($y + $h - $d), [single]$d, [single]$d, 90, 90)
    $p.CloseFigure()
    $g.DrawPath($pen, $p); $p.Dispose()
}
function Dot($g, $fill, [double]$x, [double]$y, [double]$r) { $g.FillEllipse($fill, [single]($x - $r), [single]($y - $r), [single](2*$r), [single](2*$r)) }
function EraseLine($g, $erase, [double[]]$xy) {
    $old = $g.CompositingMode; $g.CompositingMode = 'SourceCopy'
    Lines $g $erase $xy
    $g.CompositingMode = $old
}

Icon 'interact' { param($g,$pen,$fill,$erase)   # el
    Lines $g $pen @(24,34, 24,14)
    Lines $g $pen @(31,32, 31,9)
    Lines $g $pen @(38,32, 38,11)
    Lines $g $pen @(45,34, 45,17)
    Lines $g $pen @(45,34, 45,42)
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddBezier((P 45 42), (P 45 50), (P 40 56), (P 32 56))
    $p.AddLine((P 32 56), (P 28 56))
    $p.AddBezier((P 28 56), (P 22 56), (P 19 52), (P 16 46))
    $p.AddLine((P 16 46), (P 10 34))
    $p.AddBezier((P 10 34), (P 8 30), (P 14 27), (P 17 31))
    $p.AddLine((P 17 31), (P 24 40))
    $g.DrawPath($pen, $p); $p.Dispose()
}
Icon 'eye' { param($g,$pen,$fill,$erase)
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddBezier((P 6 32), (P 16 16), (P 48 16), (P 58 32))
    $p.AddBezier((P 58 32), (P 48 48), (P 16 48), (P 6 32))
    $g.DrawPath($pen, $p); $p.Dispose()
    $g.DrawEllipse($pen, 24, 24, 16, 16)
    Dot $g $fill 32 32 3
}
Icon 'camera' { param($g,$pen,$fill,$erase)   # kayıt kamerası
    RRect $g $pen 7 20 34 26 4
    $g.DrawPolygon($pen, [System.Drawing.PointF[]]@((P 41 28), (P 56 20), (P 56 46), (P 41 38)))
    Dot $g $fill 16 28 3.5
}
Icon 'brush' { param($g,$pen,$fill,$erase)
    $g.TranslateTransform(32, 32); $g.RotateTransform(40); $g.TranslateTransform(-32, -32)
    Lines $g $pen @(32,4, 32,26)
    $g.DrawRectangle($pen, 26, 26, 12, 9)
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddLine((P 26 35), (P 26 42))
    $p.AddBezier((P 26 42), (P 26 50), (P 30 56), (P 32 60))
    $p.AddBezier((P 32 60), (P 34 56), (P 38 50), (P 38 42))
    $p.AddLine((P 38 42), (P 38 35))
    $g.DrawPath($pen, $p); $p.Dispose()
    $g.ResetTransform()
}
Icon 'eraser' { param($g,$pen,$fill,$erase)
    $g.TranslateTransform(32, 30); $g.RotateTransform(-35); $g.TranslateTransform(-32, -30)
    RRect $g $pen 12 20 40 20 3
    Lines $g $pen @(26,20, 26,40)
    $g.ResetTransform()
    Lines $g $pen @(10,56, 54,56)
}
Icon 'undo' { param($g,$pen,$fill,$erase)
    Lines $g $pen @(22,12, 12,22, 22,32)
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddLine((P 12 22), (P 38 22))
    $p.AddArc(24, 22, 30, 30, 270, 180)
    $p.AddLine((P 39 52), (P 22 52))
    $g.DrawPath($pen, $p); $p.Dispose()
}
Icon 'bag' { param($g,$pen,$fill,$erase)   # ressam çantası
    RRect $g $pen 8 24 48 30 5
    $g.DrawArc($pen, 21, 10, 22, 26, 180, 180)
    Lines $g $pen @(8,35, 56,35)
    $g.DrawRectangle($pen, 28, 31, 8, 8)
}
Icon 'money' { param($g,$pen,$fill,$erase)
    RRect $g $pen 6 18 52 30 3
    $g.DrawEllipse($pen, 24, 25, 16, 16)
    Dot $g $fill 14 26 2.5
    Dot $g $fill 50 40 2.5
}
Icon 'lock' { param($g,$pen,$fill,$erase)   # kilit / maymuncuk
    RRect $g $pen 14 28 36 26 3
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddLine((P 21 28), (P 21 20))
    $p.AddArc(21, 9, 22, 22, 180, 180)
    $p.AddLine((P 43 20), (P 43 28))
    $g.DrawPath($pen, $p); $p.Dispose()
    Dot $g $fill 32 38 3.5
    Lines $g $pen @(32,40, 32,46)
}
Icon 'lockpick' { param($g,$pen,$fill,$erase)
    Lines $g $pen @(10,54, 40,24)
    Lines $g $pen @(40,24, 46,24, 46,18, 52,18, 52,12)
    $g.DrawEllipse($pen, 6, 46, 12, 12)
}
Icon 'door' { param($g,$pen,$fill,$erase)
    Lines $g $pen @(16,56, 16,8, 48,8, 48,56)
    $g.DrawPolygon($pen, [System.Drawing.PointF[]]@((P 16 8), (P 36 14), (P 36 58), (P 16 56)))
    Dot $g $fill 31 36 3
    Lines $g $pen @(8,56, 56,56)
}
Icon 'painting' { param($g,$pen,$fill,$erase)
    Lines $g $pen @(22,14, 32,5, 42,14)
    $g.DrawRectangle($pen, 7, 14, 50, 40)
    Lines $g $pen @(14,46, 26,32, 34,40, 41,33, 50,44)
    Dot $g $fill 43 24 3.5
}
Icon 'vase' { param($g,$pen,$fill,$erase)
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddLine((P 24 7), (P 40 7))
    $p.AddBezier((P 40 7), (P 37 12), (P 37 16), (P 39 19))
    $p.AddBezier((P 39 19), (P 54 28), (P 52 48), (P 42 56))
    $p.AddLine((P 42 56), (P 22 56))
    $p.AddBezier((P 22 56), (P 12 48), (P 10 28), (P 25 19))
    $p.AddBezier((P 25 19), (P 27 16), (P 27 12), (P 24 7))
    $g.DrawPath($pen, $p); $p.Dispose()
    Lines $g $pen @(17,34, 47,34)
}
Icon 'bust' { param($g,$pen,$fill,$erase)
    $g.DrawEllipse($pen, 22, 6, 20, 24)
    Lines $g $pen @(28,30, 27,36)
    Lines $g $pen @(36,30, 37,36)
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddBezier((P 11 46), (P 12 39), (P 20 36), (P 27 36))
    $p.AddLine((P 27 36), (P 37 36))
    $p.AddBezier((P 37 36), (P 44 36), (P 52 39), (P 53 46))
    $p.AddLine((P 53 46), (P 11 46))
    $g.DrawPath($pen, $p); $p.Dispose()
    $g.DrawRectangle($pen, 20, 46, 24, 10)
}
Icon 'ring' { param($g,$pen,$fill,$erase)
    $g.DrawEllipse($pen, 15, 25, 34, 32)
    $g.DrawPolygon($pen, [System.Drawing.PointF[]]@((P 23 15), (P 28 8), (P 36 8), (P 41 15), (P 32 25)))
    Lines $g $pen @(23,15, 41,15)
}
Icon 'mic' { param($g,$pen,$fill,$erase)
    RRect $g $pen 24 6 16 30 8
    $g.DrawArc($pen, 15, 18, 34, 28, 0, 180)
    Lines $g $pen @(32,46, 32,56)
    Lines $g $pen @(22,56, 42,56)
}
Icon 'mic_off' { param($g,$pen,$fill,$erase)
    RRect $g $pen 24 6 16 30 8
    $g.DrawArc($pen, 15, 18, 34, 28, 0, 180)
    Lines $g $pen @(32,46, 32,56)
    Lines $g $pen @(22,56, 42,56)
    EraseLine $g $erase @(10,8, 54,56)
    Lines $g $pen @(10,8, 54,56)
}
Icon 'speaker' { param($g,$pen,$fill,$erase)
    $g.DrawPolygon($pen, [System.Drawing.PointF[]]@((P 7 24), (P 17 24), (P 30 12), (P 30 52), (P 17 40), (P 7 40)))
    $g.DrawArc($pen, 26, 22, 18, 20, -55, 110)
    $g.DrawArc($pen, 24, 12, 32, 40, -55, 110)
}
Icon 'check' { param($g,$pen,$fill,$erase)
    Lines $g $pen @(11,34, 25,48, 53,17)
}
Icon 'warning' { param($g,$pen,$fill,$erase)
    $g.DrawPolygon($pen, [System.Drawing.PointF[]]@((P 32 8), (P 58 54), (P 6 54)))
    Lines $g $pen @(32,24, 32,38)
    Dot $g $fill 32 46 3
}
Icon 'alarm' { param($g,$pen,$fill,$erase)   # zil
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddBezier((P 16 44), (P 18 34), (P 16 14), (P 32 12))
    $p.AddBezier((P 32 12), (P 48 14), (P 46 34), (P 48 44))
    $g.DrawPath($pen, $p); $p.Dispose()
    Lines $g $pen @(11,44, 53,44)
    $g.DrawArc($pen, 26, 44, 12, 10, 0, 180)
    Lines $g $pen @(6,16, 11,21)
    Lines $g $pen @(58,16, 53,21)
    Lines $g $pen @(4,30, 9,30)
    Lines $g $pen @(60,30, 55,30)
}
Icon 'jail' { param($g,$pen,$fill,$erase)
    $g.DrawRectangle($pen, 9, 9, 46, 46)
    Lines $g $pen @(21,9, 21,55)
    Lines $g $pen @(32,9, 32,55)
    Lines $g $pen @(43,9, 43,55)
    Lines $g $pen @(9,28, 55,28)
}
Icon 'map' { param($g,$pen,$fill,$erase)
    $g.DrawPolygon($pen, [System.Drawing.PointF[]]@((P 7 14), (P 23 8), (P 41 14), (P 57 8), (P 57 50), (P 41 56), (P 23 50), (P 7 56)))
    Lines $g $pen @(23,8, 23,50)
    Lines $g $pen @(41,14, 41,56)
}
Icon 'gear' { param($g,$pen,$fill,$erase)
    $pts = New-Object 'System.Collections.Generic.List[System.Drawing.PointF]'
    $teeth = 8
    for ($i = 0; $i -lt $teeth; $i++) {
        $a0 = ($i / $teeth) * 2 * [Math]::PI
        $st = 2 * [Math]::PI / $teeth
        foreach ($k in @(@(0.0, 17.5), @(0.12, 17.5), @(0.2, 24.0), @(0.42, 24.0), @(0.5, 17.5), @(1.0, 17.5))) {
            if ($k[0] -eq 1.0) { continue }
            $a = $a0 + $k[0] * $st
            $pts.Add((P (32 + [Math]::Cos($a) * $k[1]) (32 + [Math]::Sin($a) * $k[1])))
        }
    }
    $g.DrawPolygon($pen, $pts.ToArray())
    $g.DrawEllipse($pen, 24, 24, 16, 16)
}
Icon 'players' { param($g,$pen,$fill,$erase)
    # arkadaki kişi, sonra öndeki kişinin çevresi silinir (çizgiler üst üste binmesin), sonra öndeki kişi
    $g.DrawEllipse($pen, 36, 10, 15, 15)
    $g.DrawArc($pen, 31, 30, 26, 26, 200, 140)
    $old = $g.CompositingMode; $g.CompositingMode = 'SourceCopy'
    $clear = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::Transparent)
    $g.FillEllipse($clear, 7, 10, 29, 29)
    $g.FillPie($clear, 1, 32, 42, 42, 180, 180)
    $clear.Dispose(); $g.CompositingMode = $old
    $g.DrawEllipse($pen, 13, 16, 17, 17)
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddArc(7, 38, 30, 30, 180, 180)
    $p.CloseFigure()
    $g.DrawPath($pen, $p); $p.Dispose()
}
Icon 'chevron' { param($g,$pen,$fill,$erase)
    Lines $g $pen @(24,14, 42,32, 24,50)
}
Icon 'close' { param($g,$pen,$fill,$erase)
    Lines $g $pen @(16,16, 48,48)
    Lines $g $pen @(48,16, 16,48)
}
Icon 'clock' { param($g,$pen,$fill,$erase)
    $g.DrawEllipse($pen, 9, 9, 46, 46)
    Lines $g $pen @(32,18, 32,32, 42,38)
}
Icon 'truck' { param($g,$pen,$fill,$erase)   # kaçış / kamyon
    $g.DrawRectangle($pen, 6, 16, 32, 28)
    Lines $g $pen @(38,24, 49,24, 58,34, 58,44, 38,44)
    $g.DrawEllipse($pen, 12, 42, 11, 11)
    $g.DrawEllipse($pen, 43, 42, 11, 11)
}

Write-Host ("UI varlıkları üretildi: {0} PNG -> {1}" -f $script:count, $OutRoot)
