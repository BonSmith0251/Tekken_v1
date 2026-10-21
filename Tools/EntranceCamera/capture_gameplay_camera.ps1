# Read-only snapshot for Tekken 7 v5.10. Run from an account able to read the game.
# Pointer source: https://github.com/Kiloutre/TekkenMovesetExtractor/blob/master/game_addresses.txt
param(
 [Parameter(Mandatory=$true)][int]$GameProcessId,
 [Parameter(Mandatory=$true)][string]$Workspace,
 [Parameter(Mandatory=$true)][string]$OutputPath,
 [ValidateRange(1,240)][int]$SampleCount=120
)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $OutputPath){throw 'Choose a new output path to preserve previous captures.'}
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class CameraRead {
 [DllImport("kernel32.dll", SetLastError=true)] public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
 [DllImport("kernel32.dll", SetLastError=true)] public static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] bytes, UIntPtr count, out UIntPtr read);
 [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
 public static byte[] Read(IntPtr handle, long address, int count) {
  var bytes=new byte[count]; UIntPtr read;
  if(!ReadProcessMemory(handle,new IntPtr(address),bytes,(UIntPtr)count,out read) || read.ToUInt64()!=(ulong)count) throw new Exception("Read failed at "+address.ToString("X"));
  return bytes;
 }
}
"@
$game=Get-Process -Id $GameProcessId
$expected=Join-Path $Workspace 'TEKKEN.7\TekkenGame\Binaries\Win64\TekkenGame-Win64-Shipping.exe'
if($game.Path -ne $expected){throw 'Unexpected executable'}
$base=$game.MainModule.BaseAddress.ToInt64()
$handle=[CameraRead]::OpenProcess(0x410,$false,$GameProcessId)
if($handle -eq [IntPtr]::Zero){throw 'Read-only process access failed'}
try {
 $address=$base+0x3785470
 $chain=@()
 foreach($offset in @(0x30,0x418,0)) {
  $pointer=[BitConverter]::ToInt64([CameraRead]::Read($handle,$address,8),0)
  $chain+=('{0:X} -> {1:X}' -f $address,$pointer)
  if($pointer -lt 65536){throw ('Invalid pointer: '+($chain -join ', '))}
  $address=$pointer+$offset
 }
 $samples=@()
 for($sample=0;$sample -lt $SampleCount;$sample++){
 $bytes=[CameraRead]::Read($handle,$address,0x480)
 $values=[ordered]@{}
 foreach($entry in @(@('fov',0x39C),@('defaultAspect',0x3A8),@('viewTargetAspect',0x420),@('x',0x3F8),@('y',0x3FC),@('z',0x400),@('pitch',0x404),@('yaw',0x408),@('roll',0x40C))) {
  $values[$entry[0]]=[BitConverter]::ToSingle($bytes,$entry[1])
 }
 $nearby=[ordered]@{}
 for($i=0x380;$i -lt 0x480;$i+=4){$nearby[('{0:X}' -f $i)]=[BitConverter]::ToSingle($bytes,$i)}
 $report=[ordered]@{utc=[DateTime]::UtcNow.ToString('o');pid=$GameProcessId;moduleBase=('{0:X}' -f $base);cameraAddress=('{0:X}' -f $address);chain=$chain;values=$values;readOnly=$true;nearbyFloats=$nearby}
 $samples+=$report
 if($SampleCount -gt 1){Start-Sleep -Milliseconds 250}
 }
 $samples | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
 $samples | ForEach-Object { [PSCustomObject]@{utc=$_.utc; fov=$_.values.fov; aspect=$_.values.viewTargetAspect; pitch=$_.values.pitch} } | Format-Table
} finally {[void][CameraRead]::CloseHandle($handle)}
