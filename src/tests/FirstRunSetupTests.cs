using System;
using System.ComponentModel;
using System.IO;
using LeigodGuard;

class FirstRunSetupTests {
 static int count;
 static void Check(bool condition,string message){if(!condition)throw new Exception("FAIL: "+message);count++;Console.WriteLine("PASS: "+message);}
 static void Main(){
  int saves=0,installs=0,elevations=0;string argument=null,kind=null;
  Action<Settings> save=s=>saves++;
  Func<string,bool> elevate=a=>{elevations++;argument=a;return true;};
  Action install=()=>installs++;
  Action<string,string> report=(k,m)=>kind=k;
  var settings=new Settings();
  Check(settings.Games.Count==0&&!settings.FirstRunSetupCompleted,"fresh application has no preset game and requires initial setup");
  bool handoff=FirstRunSetup.Run(settings,false,false,false,save,elevate,install,report);
  Check(handoff&&settings.FirstRunSetupCompleted&&saves==1&&installs==0&&elevations==1&&argument=="--takeover --first-run-setup","first ordinary launch saves state and requests one elevated foreground restart");
  Check(!FirstRunSetup.Run(settings,true,true,false,save,elevate,install,report)&&installs==1&&kind=="first_run_setup_completed","elevated continuation installs startup without relaunching the visible application");
  Check(!FirstRunSetup.Run(settings,false,true,false,save,elevate,install,report)&&installs==1&&elevations==1,"later launch preserves user-disabled startup instead of reinstalling");
  var adminSettings=new Settings();
  FirstRunSetup.Run(adminSettings,false,true,false,save,elevate,install,report);
  Check(installs==2&&elevations==1&&adminSettings.FirstRunSetupCompleted,"already elevated first launch enables startup without a permission prompt");
  var backgroundSettings=new Settings();FirstRunSetup.Run(backgroundSettings,false,false,true,save,elevate,install,report);
  Check(argument.EndsWith(" --background"),"initial setup preserves background launch mode");
  var cancelled=new Settings();int declined=0;
  Check(!FirstRunSetup.Run(cancelled,false,false,false,save,a=>{declined++;throw new Win32Exception(1223);},install,report)&&kind=="first_run_permission_declined"&&cancelled.FirstRunSetupCompleted,"Windows permission cancellation keeps application open and records the decline");
  FirstRunSetup.Run(cancelled,false,false,false,save,a=>{declined++;return true;},install,report);
  Check(declined==1,"declined initial permission is not requested repeatedly on later launches");
  FirstRunSetup.Run(new Settings(),false,true,false,save,elevate,()=>{throw new IOException("test failure");},report);
  Check(kind=="first_run_setup_failed","startup installation failure is reported without blocking monitor launch");
  FirstRunSetup.Run(new Settings(),true,false,false,save,elevate,install,report);
  Check(kind=="first_run_setup_failed"&&elevations==2,"continuation without elevation cannot create a permission loop");
  var folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"first-run-test-data");Directory.CreateDirectory(folder);
  typeof(Store).GetField("Folder").SetValue(null,folder);typeof(Store).GetField("Config").SetValue(null,Path.Combine(folder,"settings.json"));
  File.WriteAllText(Store.Config,"{\"ExitDelaySeconds\":0,\"AutoDiscover\":false,\"Games\":[],\"IgnoredGames\":[],\"SteamLibraries\":[]}");
  var legacy=Store.Load();Check(legacy.FirstRunSetupCompleted&&!legacy.AutoDiscover&&legacy.ExitDelaySeconds==0&&Store.Read<Settings>(Store.Config).FirstRunSetupCompleted,"legacy user configuration is migrated without forcing startup or resetting choices");
  Store.Write(Store.Config,new Settings());Check(!Store.Load().FirstRunSetupCompleted,"unfinished fresh setup remains pending after settings are saved");
  Console.WriteLine("FIRST RUN TOTAL: "+count+" passed");
 }
}
