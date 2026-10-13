using System;
using System.IO;
using LeigodGuard;
class UninstallTests {
 [System.Runtime.InteropServices.DllImport("kernel32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode)] static extern uint GetShortPathName(string path,System.Text.StringBuilder text,uint size);
 static int count;
 static void Check(bool value,string label){if(!value)throw new Exception("FAIL: "+label);count++;Console.WriteLine("PASS: "+label);}
 static bool Rejected(Action action){try{action();return false;}catch(InvalidOperationException){return true;}catch(IOException){return true;}}
 static void Main(){
  var root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"uninstall-fixture");Directory.CreateDirectory(root);var own=Path.Combine(root,"assistant.exe");var helper=Path.Combine(root,"helper.exe");File.WriteAllText(own,"disposable-test-application");File.Copy(own,helper,true);var request=new UninstallRequest{ExecutablePath=own,OwnerSid="test-user",ParentProcessId=1,RemoveData=true};
  Check(UninstallAction.ValidateRequest(request,helper,"test-user")==own,"uninstall accepts only a matching application binary owned by the same user");
  var alias=new System.Text.StringBuilder(32768);uint aliasLength=GetShortPathName(own,alias,(uint)alias.Capacity);Check(aliasLength>0&&UninstallAction.SamePath(alias.ToString(),own),"Windows short and long paths identify the same installation");
  Check(Rejected(()=>UninstallAction.ValidateRequest(request,helper,"other-user")),"another Windows user cannot remove this profile");
  request.ExecutablePath=helper;Check(Rejected(()=>UninstallAction.ValidateRequest(request,helper,"test-user")),"helper cannot be its own removal target");
  request.ExecutablePath="relative.exe";Check(Rejected(()=>UninstallAction.ValidateRequest(request,helper,"test-user")),"relative deletion target is rejected");
  request.ExecutablePath=own;File.WriteAllText(own,"unrelated-binary");Check(Rejected(()=>UninstallAction.ValidateRequest(request,helper,"test-user")),"unrelated or changed binary cannot be removed");File.Copy(helper,own,true);
  Check(UninstallAction.SamePath(own,Path.Combine(root,".","assistant.exe"))&&!UninstallAction.SamePath(own,helper),"startup ownership comparison resolves paths but distinguishes another installation");
  var data=Path.Combine(root,"LeigodGuard");Directory.CreateDirectory(Path.Combine(data,"requests"));File.WriteAllText(Path.Combine(data,"settings.json"),"test settings");File.WriteAllText(Path.Combine(data,"requests","test.json"),"test request");var adjacent=Path.Combine(root,"personal.txt");File.WriteAllText(adjacent,"keep");UninstallAction.DeleteData(data);Check(!Directory.Exists(data)&&File.Exists(adjacent),"only dedicated settings and request directory is removed");
  var other=Path.Combine(root,"OtherData");Directory.CreateDirectory(other);Check(Rejected(()=>UninstallAction.DeleteData(other))&&Directory.Exists(other),"uninstall does not accept a different data directory");
  var install=Path.Combine(root,"Install");Directory.CreateDirectory(install);var exe=Path.Combine(install,"assistant.exe");File.Copy(helper,exe,true);File.WriteAllText(Path.Combine(install,"使用说明.txt"),"雷神自动暂停助手 2.4\n说明");File.WriteAllText(Path.Combine(install,"运行状态.json"),"{}");File.WriteAllText(Path.Combine(install,"恢复检查.json"),"{}");File.WriteAllText(Path.Combine(install,"personal.txt"),"keep");UninstallAction.DeleteProgramFiles(exe);Check(!File.Exists(exe)&&!File.Exists(Path.Combine(install,"运行状态.json"))&&!File.Exists(Path.Combine(install,"恢复检查.json"))&&!File.Exists(Path.Combine(install,"使用说明.txt")),"uninstall removes application executable and its known companion files");Check(File.Exists(Path.Combine(install,"personal.txt"))&&Directory.Exists(install),"uninstall preserves adjacent personal files and their parent directory");
  var empty=Path.Combine(root,"EmptyInstall");Directory.CreateDirectory(empty);exe=Path.Combine(empty,"assistant.exe");File.Copy(helper,exe,true);UninstallAction.DeleteProgramFiles(exe);Check(!Directory.Exists(empty),"empty application folder can be removed without recursive installation cleanup");
  Console.WriteLine("UNINSTALL TOTAL: "+count+" passed; isolated files only, no real task or user profile touched.");
 }
}

