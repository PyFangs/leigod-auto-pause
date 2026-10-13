using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Text;
using System.Windows.Forms;

namespace LeigodGuard {
public class UninstallRequest {
 public string ExecutablePath {get;set;}
 public string OwnerSid {get;set;}
 public int ParentProcessId {get;set;}
 public bool RemoveData {get;set;}
}
public static class UninstallAction {
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool MoveFileEx(string existing,string target,int flags);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern uint GetLongPathName(string path,StringBuilder text,uint size);
 public static string CanonicalPath(string path){var full=Path.GetFullPath(path);var text=new StringBuilder(32768);var length=GetLongPathName(full,text,(uint)text.Capacity);return length>0&&length<text.Capacity?text.ToString():full;}
 public static bool SamePath(string a,string b){try{return string.Equals(CanonicalPath(a),CanonicalPath(b),StringComparison.OrdinalIgnoreCase);}catch{return false;}}
 static string Hash(string path){using(var stream=File.OpenRead(path))using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(stream));}
 public static string ValidateRequest(UninstallRequest request,string helper,string sid){
  if(request==null||request.OwnerSid!=sid)throw new InvalidOperationException("请使用运行助手的同一个 Windows 用户卸载。");
  if(string.IsNullOrWhiteSpace(request.ExecutablePath)||!Path.IsPathRooted(request.ExecutablePath))throw new InvalidOperationException("卸载程序路径无效。");
  var executable=CanonicalPath(request.ExecutablePath);
  if(!executable.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)||SamePath(executable,helper)||executable.StartsWith("\\\\",StringComparison.Ordinal)||Discovery.Inside(executable,Environment.GetFolderPath(Environment.SpecialFolder.Windows)))throw new InvalidOperationException("卸载目标不允许删除。");
  if(!File.Exists(executable)||!File.Exists(helper)||Hash(executable)!=Hash(helper))throw new InvalidOperationException("程序文件已变更，停止卸载。");
  if((File.GetAttributes(executable)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("程序路径包含链接，停止卸载。");
  var directory=new DirectoryInfo(Path.GetDirectoryName(executable));for(var parent=directory;parent!=null;parent=parent.Parent)if((parent.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("程序目录包含链接，停止卸载。");
  return executable;
 }
 static void CheckTree(string path,int depth,ref int entries){
  if(depth>32||++entries>10000)throw new IOException("设置目录过于复杂，请手动清理。");
  if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("设置目录包含链接，请手动清理。");
  if(Directory.Exists(path))foreach(var entry in Directory.GetFileSystemEntries(path))CheckTree(entry,depth+1,ref entries);
 }
 public static void DeleteData(string directory){if(!Directory.Exists(directory))return;var root=Path.GetFullPath(directory);if(!string.Equals(Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar)),"LeigodGuard",StringComparison.OrdinalIgnoreCase))throw new IOException("设置目录不属于助手，停止清理。");int entries=0;CheckTree(root,0,ref entries);Directory.Delete(root,true);}
 public static void DeleteProgramFiles(string executable){
  var root=Path.GetDirectoryName(Path.GetFullPath(executable));
  foreach(var name in new[]{"运行状态.json","恢复检查.json"}){var file=Path.Combine(root,name);if(File.Exists(file)&&(File.GetAttributes(file)&FileAttributes.ReparsePoint)==0)File.Delete(file);}
  var readme=Path.Combine(root,"使用说明.txt");if(File.Exists(readme)&&(File.GetAttributes(readme)&FileAttributes.ReparsePoint)==0&&new FileInfo(readme).Length<100000&&File.ReadAllText(readme).StartsWith("雷神自动暂停助手 ",StringComparison.Ordinal))File.Delete(readme);
  File.Delete(executable);
  // Only an empty directory can be removed; never recursively remove an installation folder.
  try{if(Directory.GetFileSystemEntries(root).Length==0)Directory.Delete(root,false);}catch(IOException){}catch(UnauthorizedAccessException){}
 }
 public static Process Start(){
  var directory=Path.Combine(CanonicalPath(Path.GetTempPath()),"LeigodGuard-Uninstall-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);var helper=Path.Combine(directory,"uninstall.exe");var requestFile=Path.Combine(directory,"request.json");
  try{File.Copy(Store.Exe,helper);Store.Write(requestFile,new UninstallRequest{ExecutablePath=Store.Exe,OwnerSid=Store.Sid,ParentProcessId=Process.GetCurrentProcess().Id,RemoveData=true});var info=new ProcessStartInfo(helper,"--uninstall-worker "+Store.Quote(requestFile)){UseShellExecute=true,WindowStyle=ProcessWindowStyle.Hidden};if(!Store.IsAdmin)info.Verb="runas";var process=Process.Start(info);if(process==null)throw new IOException("没有启动卸载程序。");return process;}
  catch{try{File.Delete(requestFile);File.Delete(helper);Directory.Delete(directory,false);}catch{}throw;}
 }
 public static void Run(string requestFile){
  var helper=CanonicalPath(Store.Exe);var directory=Path.GetDirectoryName(helper);Guid id;var prefix="LeigodGuard-Uninstall-";var name=Path.GetFileName(directory);
  if(!Discovery.Inside(helper,CanonicalPath(Path.GetTempPath()))||!name.StartsWith(prefix,StringComparison.Ordinal)||!Guid.TryParse(name.Substring(prefix.Length),out id)||!SamePath(requestFile,Path.Combine(directory,"request.json")))throw new InvalidOperationException("卸载请求位置无效。");
  if(!Store.IsAdmin)throw new InvalidOperationException("卸载需要完成 Windows 权限确认。");
  var request=Store.Read<UninstallRequest>(requestFile);var executable=ValidateRequest(request,helper,Store.Sid);
  Process parent=null;try{parent=Process.GetProcessById(request.ParentProcessId);}catch(ArgumentException){}
  using(parent){if(parent!=null&&!parent.HasExited&&!SamePath(Native.ProcessPath((uint)parent.Id),executable))throw new InvalidOperationException("运行中的程序与卸载目标不符。");Startup.RemoveFor(executable);if(parent!=null&&!parent.HasExited){Program.SignalQuit();if(!parent.WaitForExit(15000))throw new IOException("助手仍在退出，请稍后重试卸载。");}}
  if(request.RemoveData)DeleteData(Store.Folder);
  for(int attempt=0;;attempt++){try{DeleteProgramFiles(executable);break;}catch(IOException){if(attempt>=19)throw;Thread.Sleep(250);}}
  try{File.Delete(requestFile);}catch(IOException){}
  // Windows removes the running temporary helper after reboot without launching a shell script.
  bool queued=MoveFileEx(helper,null,4);if(queued)MoveFileEx(directory,null,4);
  MessageBox.Show(queued?"助手已卸载。\n临时卸载文件将在下次重启 Windows 后自动清理。":"助手已卸载。\n临时卸载文件留在 Windows 临时目录，可在重启后清理。","卸载完成",MessageBoxButtons.OK,MessageBoxIcon.Information);
 }
}
}

