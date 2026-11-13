#
# Copyright 2026 Xiayue
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
#     http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.
#
param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\src\StartupManager\Assets\app.ico')
)

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Drawing.Common -ErrorAction SilentlyContinue

$size = 256
$bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::Transparent)

$rect = [System.Drawing.RectangleF]::new(8, 8, 240, 240)
$path = [System.Drawing.Drawing2D.GraphicsPath]::new()
$radius = 58
$diameter = $radius * 2
$path.AddArc($rect.X, $rect.Y, $diameter, $diameter, 180, 90)
$path.AddArc($rect.Right - $diameter, $rect.Y, $diameter, $diameter, 270, 90)
$path.AddArc($rect.Right - $diameter, $rect.Bottom - $diameter, $diameter, $diameter, 0, 90)
$path.AddArc($rect.X, $rect.Bottom - $diameter, $diameter, $diameter, 90, 90)
$path.CloseFigure()

$gradient = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
    [System.Drawing.PointF]::new(20, 20),
    [System.Drawing.PointF]::new(236, 236),
    [System.Drawing.Color]::FromArgb(255, 79, 70, 229),
    [System.Drawing.Color]::FromArgb(255, 13, 184, 166)
)
$graphics.FillPath($gradient, $path)

# subtle top highlight
$highlight = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
    [System.Drawing.PointF]::new(0, 0),
    [System.Drawing.PointF]::new(0, 150),
    [System.Drawing.Color]::FromArgb(70, 255, 255, 255),
    [System.Drawing.Color]::FromArgb(0, 255, 255, 255)
)
$graphics.FillPath($highlight, $path)

# lightning bolt
$bolt = [System.Drawing.Drawing2D.GraphicsPath]::new()
$points = [System.Drawing.PointF[]]@(
    [System.Drawing.PointF]::new(148, 38),
    [System.Drawing.PointF]::new(78, 139),
    [System.Drawing.PointF]::new(122, 139),
    [System.Drawing.PointF]::new(104, 218),
    [System.Drawing.PointF]::new(180, 109),
    [System.Drawing.PointF]::new(134, 109)
)
$bolt.AddPolygon($points)
$white = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 255, 255, 255))
$shadow = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(55, 0, 0, 0))
$shadowMatrix = [System.Drawing.Drawing2D.Matrix]::new()
$shadowMatrix.Translate(5, 7)
$shadowPath = $bolt.Clone()
$shadowPath.Transform($shadowMatrix)
$graphics.FillPath($shadow, $shadowPath)
$graphics.FillPath($white, $bolt)

# small orbit dot
$dotBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(210, 255, 255, 255))
$graphics.FillEllipse($dotBrush, 182, 50, 20, 20)

$pngPath = [System.IO.Path]::ChangeExtension($OutputPath, '.png')
$directory = [System.IO.Path]::GetDirectoryName($OutputPath)
if (-not [System.IO.Directory]::Exists($directory)) {
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
}
$bitmap.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)

# Build a PNG-compressed ICO (Windows Vista+ supports PNG payloads).
$pngBytes = [System.IO.File]::ReadAllBytes($pngPath)
$stream = [System.IO.File]::Create($OutputPath)
$writer = [System.IO.BinaryWriter]::new($stream)
$writer.Write([UInt16]0)       # reserved
$writer.Write([UInt16]1)       # icon
$writer.Write([UInt16]1)       # image count
$writer.Write([Byte]0)         # width 256
$writer.Write([Byte]0)         # height 256
$writer.Write([Byte]0)         # palette
$writer.Write([Byte]0)         # reserved
$writer.Write([UInt16]1)       # planes
$writer.Write([UInt16]32)      # bpp
$writer.Write([UInt32]$pngBytes.Length)
$writer.Write([UInt32]22)      # offset
$writer.Write($pngBytes)
$writer.Dispose()
$stream.Dispose()

$graphics.Dispose()
$bitmap.Dispose()
Write-Output "Icon written to $OutputPath"
Write-Output "PNG preview: $pngPath"
