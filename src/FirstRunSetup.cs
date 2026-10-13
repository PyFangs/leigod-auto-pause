using System;
using System.ComponentModel;

namespace LeigodGuard {
public static class FirstRunSetup {
 // Delegates keep permission and scheduler operations out of tests.
 // Record the attempt first so declining Windows permission does not create a prompt loop.
 public static bool Run(Settings settings,bool resumed,bool admin,bool background,Action<Settings> save,Func<string,bool> elevate,Action install,Action<string,string> report) {
  if(settings.FirstRunSetupCompleted&&!resumed)return false;
  settings.FirstRunSetupCompleted=true;save(settings);
  if(!admin) {
   if(resumed){report("first_run_setup_failed","未获得管理员权限");return false;}
   try {if(elevate("--takeover --first-run-setup"+(background?" --background":"")))return true;report("first_run_setup_failed","未能启动权限确认");}
   catch(Win32Exception e){report(e.NativeErrorCode==1223?"first_run_permission_declined":"first_run_setup_failed",e.Message);}
   catch(Exception e){report("first_run_setup_failed",e.Message);}
   return false;
  }
  try {install();report("first_run_setup_completed","开机后台运行已启用");}
  catch(Exception e){report("first_run_setup_failed",e.Message);}
  return false;
 }
}
}
