using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace LeigodGuard {
public class LocalGameInfo {
 public string Name,EnglishName,SteamId,LocalPath;
 public string[] Processes=new string[0];
}
public class DiscoveryResult {
 public List<GameBinding> Games=new List<GameBinding>();
 public int InstalledSteamGames,LocalMappings;
 public string Error;
}
public static class Discovery {
 public static bool Auxiliary(string name) {
  var n=(name??"").ToLowerInvariant();
  return new[]{"steam.exe","steamwebhelper.exe","leigod.exe","explorer.exe","start_protected_game.exe"}.Contains(n)||new[]{"crash","launcher","unins","report","anticheat","battleye","easyanticheat","cef","webhelper","updater","vc_redist","dxsetup","overlay","unitycrash","bootstrap"}.Any(n.Contains);
 }
 public static bool Inside(string file,string directory) {
  try {if(string.IsNullOrWhiteSpace(file)||string.IsNullOrWhiteSpace(directory))return false;var dir=Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;return Path.GetFullPath(file).StartsWith(dir,StringComparison.OrdinalIgnoreCase);}catch{return false;}
 }
 public static string DetectClientPath() {
  try {var h=Native.FindClient();uint id;Native.GetWindowThreadProcessId(h,out id);var path=Native.ProcessPath(id);if(!string.IsNullOrEmpty(path)){var launcher=Path.Combine(Path.GetDirectoryName(path),"leigod_launcher.exe");return File.Exists(launcher)?launcher:path;}}catch{}
  foreach(var root in new[]{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)}) {var path=Path.Combine(root,"LeiGod_Acc","leigod_launcher.exe");if(File.Exists(path))return path;}
  return "";
 }
 public static List<string> SteamRoots() {
  var roots=new List<string>();
  foreach(var hive in new[]{RegistryHive.CurrentUser,RegistryHive.LocalMachine})foreach(var view in new[]{RegistryView.Registry32,RegistryView.Registry64})try{using(var baseKey=RegistryKey.OpenBaseKey(hive,view))using(var key=baseKey.OpenSubKey("Software\\Valve\\Steam")){if(key==null)continue;var path=Convert.ToString(key.GetValue("SteamPath")??key.GetValue("InstallPath"));if(Directory.Exists(path))roots.Add(Path.GetFullPath(path));}}catch{}
  var standard=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam");if(Directory.Exists(standard))roots.Add(standard);
  return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
 }
 public static List<string> CacheFolders() {
  var result=new List<string>();var roaming=Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
  try {foreach(var profile in Directory.GetDirectories(roaming,"leigod*")){var indexed=Path.Combine(profile,"IndexedDB");if(Directory.Exists(indexed))result.AddRange(Directory.GetDirectories(indexed,"*.leveldb"));}}catch{}
  return result;
 }
 public static DiscoveryResult Scan() {return Scan(SteamRoots(),CacheFolders());}
 public static DiscoveryResult Scan(IEnumerable<string> steamRoots,IEnumerable<string> cacheFolders) {
  var result=new DiscoveryResult();var metadata=ReadMetadata(cacheFolders);result.LocalMappings=metadata.Count;
  var libraries=new HashSet<string>(steamRoots,StringComparer.OrdinalIgnoreCase);
  foreach(var root in steamRoots.ToArray())try{
   var file=Path.Combine(root,"steamapps","libraryfolders.vdf");if(!File.Exists(file))continue;
   var vdf=Vdf.Parse(ReadText(file));foreach(var path in Vdf.Values(vdf,"path"))if(Directory.Exists(path))libraries.Add(path);
   // Older libraryfolders.vdf stores paths directly under numbered keys.
   foreach(var path in Vdf.NumberedPaths(vdf))if(Directory.Exists(path))libraries.Add(path);
  }catch{}
  foreach(var library in libraries) {
   var apps=Path.Combine(library,"steamapps");if(!Directory.Exists(apps))continue;
   foreach(var manifest in Directory.GetFiles(apps,"appmanifest_*.acf"))try{
    var data=Vdf.Parse(ReadText(manifest));var id=Vdf.Values(data,"appid").FirstOrDefault();var name=Vdf.Values(data,"name").FirstOrDefault();var install=Vdf.Values(data,"installdir").FirstOrDefault();
    ulong number;if(!ulong.TryParse(id,out number)||number==0||string.IsNullOrWhiteSpace(name)||string.IsNullOrWhiteSpace(install))continue;
    var common=Path.Combine(apps,"common");var dir=Path.GetFullPath(Path.Combine(common,install));if(!Inside(dir,common)||!Directory.Exists(dir))continue;
    if(name.IndexOf("Redistributable",StringComparison.OrdinalIgnoreCase)>=0||name.IndexOf("Steamworks",StringComparison.OrdinalIgnoreCase)>=0||new[]{"Wallpaper Engine","Lossless Scaling","Soundpad","OBS Studio"}.Contains(name,StringComparer.OrdinalIgnoreCase))continue;
    result.InstalledSteamGames++;
    var matches=metadata.Where(g=>g.SteamId==id).ToList();var titles=matches.Select(g=>g.Name).Where(s=>!string.IsNullOrWhiteSpace(s)).ToArray();
    var aliases=titles.Concat(matches.Select(g=>g.EnglishName)).Concat(new[]{name}).Where(s=>!string.IsNullOrWhiteSpace(s)&&s.Length>=2).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    var processes=matches.SelectMany(g=>g.Processes).Where(p=>!Auxiliary(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    result.Games.Add(new GameBinding{Name=titles.FirstOrDefault(t=>Client.Normalize(t)==Client.Normalize(name))??titles.FirstOrDefault()??name,Aliases=aliases,Processes=processes,Enabled=true,AutoDiscovered=true,SteamAppId=id,InstallDirectory=dir});
   }catch{}
  }
  foreach(var game in metadata.Where(g=>string.IsNullOrEmpty(g.SteamId)&&!string.IsNullOrWhiteSpace(g.LocalPath)))try{
   var path=game.LocalPath.Trim('"');if(!File.Exists(path)||!path.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)||Auxiliary(Path.GetFileName(path)))continue;
   result.Games.Add(new GameBinding{Name=game.Name,Aliases=new[]{game.Name,game.EnglishName}.Where(s=>!string.IsNullOrWhiteSpace(s)&&s.Length>=2).Distinct().ToArray(),Processes=new[]{Path.GetFileName(path)},Enabled=true,AutoDiscovered=true,ExecutablePath=Path.GetFullPath(path)});
  }catch{}
  result.Games=result.Games.GroupBy(g=>g.DiscoveryKey,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).OrderBy(g=>g.Name,StringComparer.CurrentCultureIgnoreCase).ToList();return result;
 }
 static string ReadText(string file) {if(new FileInfo(file).Length>2*1024*1024)throw new IOException("本地清单过大");return File.ReadAllText(file,Encoding.UTF8);}
 public static List<LocalGameInfo> ReadMetadata(IEnumerable<string> folders) {
  var games=new List<LocalGameInfo>();
  foreach(var folder in folders)try{foreach(var file in Directory.GetFiles(folder).Where(p=>p.EndsWith(".log",StringComparison.OrdinalIgnoreCase)||p.EndsWith(".ldb",StringComparison.OrdinalIgnoreCase)).OrderBy(File.GetLastWriteTimeUtc)){
   try {foreach(var bytes in LocalBlocks.Values(file))games.AddRange(ParseMetadata(bytes));}catch{}
  }}catch{}
  return games.GroupBy(g=>(g.Name??"")+"|"+(g.SteamId??"")+"|"+(g.LocalPath??""),StringComparer.OrdinalIgnoreCase).Select(g=>g.Last()).ToList();
 }
 public static List<LocalGameInfo> ParseMetadata(byte[] data) {
  var games=new List<LocalGameInfo>();
  for(int i=0;i<data.Length-8;i++) {
   if(data[i]!='o'||data[i+1]!='"'||data[i+2]!=2||data[i+3]!='i'||data[i+4]!='d')continue;
   var reader=new V8Reader(data,i);var values=reader.ObjectFields();
   object title,processes,launch,local,english;values.TryGetValue("title",out title);values.TryGetValue("game_process",out processes);values.TryGetValue("client_launch_config_v11",out launch);values.TryGetValue("localpath",out local);values.TryGetValue("game_title_en",out english);
   var name=title as string;if(string.IsNullOrWhiteSpace(name)||name.Length>160)continue;
   var launcher=launch as Dictionary<string,object>;string uri=null;if(launcher!=null)uri=launcher.Values.OfType<string>().FirstOrDefault(s=>Regex.IsMatch(s,@"^steam://(?:rungameid|run)/\d+$",RegexOptions.IgnoreCase));
   var localPath=local as string;if(uri==null&&localPath!=null&&Regex.IsMatch(localPath,@"^steam://(?:rungameid|run)/\d+$",RegexOptions.IgnoreCase))uri=localPath;
   var proc=(processes as string)??"";var names=Regex.Matches(proc,@"[\w.\-]+\.exe",RegexOptions.IgnoreCase).Cast<Match>().Select(m=>m.Value).Where(p=>!Auxiliary(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
   if(uri==null&&string.IsNullOrEmpty(localPath))continue;
   games.Add(new LocalGameInfo{Name=name,EnglishName=english as string,SteamId=uri==null?null:Regex.Match(uri,@"\d+$").Value,Processes=names,LocalPath=localPath});
  }
  return games;
 }
 public static bool Merge(Settings settings,DiscoveryResult found) {
  bool changed=false;var ignored=new HashSet<string>(settings.IgnoredGames??new string[0],StringComparer.OrdinalIgnoreCase);
  foreach(var candidate in found.Games) {
   if(ignored.Contains(candidate.DiscoveryKey)||ignored.Contains(candidate.Name))continue;
   var existing=settings.Games.FirstOrDefault(g=>g.DiscoveryKey==candidate.DiscoveryKey||((string.IsNullOrEmpty(g.SteamAppId)||string.IsNullOrEmpty(candidate.SteamAppId))&&g.Aliases.Any(a=>candidate.Aliases.Any(b=>Client.Normalize(a)==Client.Normalize(b)))));
   if(existing==null){settings.Games.Add(candidate);changed=true;continue;}
   var aliases=existing.Aliases.Concat(candidate.Aliases).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();if(aliases.Length!=existing.Aliases.Length){existing.Aliases=aliases;changed=true;}
   if(existing.AutoDiscovered&&!(existing.ManualAdded&&!string.IsNullOrEmpty(existing.ExecutablePath))&&(existing.InstallDirectory!=candidate.InstallDirectory||existing.ExecutablePath!=candidate.ExecutablePath||!existing.Processes.SequenceEqual(candidate.Processes,StringComparer.OrdinalIgnoreCase))){existing.InstallDirectory=candidate.InstallDirectory;existing.ExecutablePath=candidate.ExecutablePath;existing.Processes=candidate.Processes;changed=true;}
   if(string.IsNullOrEmpty(existing.SteamAppId)&&!string.IsNullOrEmpty(candidate.SteamAppId)){existing.SteamAppId=candidate.SteamAppId;changed=true;}
  }
  return changed;
 }
}
public static class Vdf {
 public static Dictionary<string,object> Parse(string text){int index=0;return Read(text,ref index,0);}
 static string Token(string text,ref int i){while(i<text.Length){if(char.IsWhiteSpace(text[i])){i++;continue;}if(i+1<text.Length&&text[i]=='/'&&text[i+1]=='/'){while(i<text.Length&&text[i]!='\n')i++;continue;}break;}if(i>=text.Length)return null;char c=text[i++];if(c=='{'||c=='}')return c.ToString();if(c!='"')throw new FormatException("VDF token");var s=new StringBuilder();while(i<text.Length){c=text[i++];if(c=='"')return s.ToString();if(c=='\\'&&i<text.Length&&(text[i]=='\\'||text[i]=='"'))c=text[i++];s.Append(c);}throw new FormatException("VDF quote");}
 static Dictionary<string,object> Read(string text,ref int i,int depth){if(depth>32)throw new FormatException();var result=new Dictionary<string,object>(StringComparer.OrdinalIgnoreCase);while(true){var key=Token(text,ref i);if(key==null||key=="}")return result;var value=Token(text,ref i);if(value==null)throw new FormatException();result[key]=value=="{"?(object)Read(text,ref i,depth+1):value;}}
 public static IEnumerable<string> Values(Dictionary<string,object> data,string key){foreach(var pair in data){if(pair.Key.Equals(key,StringComparison.OrdinalIgnoreCase)&&pair.Value is string)yield return (string)pair.Value;var child=pair.Value as Dictionary<string,object>;if(child!=null)foreach(var value in Values(child,key))yield return value;}}
 public static IEnumerable<string> NumberedPaths(Dictionary<string,object> data){foreach(var pair in data){int n;if(int.TryParse(pair.Key,out n)&&pair.Value is string&&Path.IsPathRooted((string)pair.Value))yield return (string)pair.Value;var child=pair.Value as Dictionary<string,object>;if(child!=null)foreach(var value in NumberedPaths(child))yield return value;}}
}
// Read-only readers: only title, launch URI, executable names and local game paths leave this layer.
class V8Reader {
 readonly byte[] data;int p,nodes;readonly List<object> refs=new List<object>();
 public V8Reader(byte[] bytes,int offset){data=bytes;p=offset;}
 uint Uint(){uint n=0;int shift=0;for(int i=0;i<5;i++){byte b=Byte();n|=(uint)(b&127)<<shift;if(b<128)return n;shift+=7;}throw new FormatException();}
 byte Byte(){if(p>=data.Length)throw new FormatException();return data[p++];}
 object Value(int depth){if(depth>32||++nodes>50000)throw new FormatException();byte tag;do{tag=Byte();}while(tag==0);switch(tag){case (byte)'"':case (byte)'S':case (byte)'c':int size=checked((int)Uint());if(size<0||size>1024*1024||p+size>data.Length)throw new FormatException();string s=(tag=='c'?Encoding.Unicode:Encoding.UTF8).GetString(data,p,size);p+=size;return s;case (byte)'I':return (int)Uint();case (byte)'U':return Uint();case (byte)'N':if(p+8>data.Length)throw new FormatException();double num=BitConverter.ToDouble(data,p);p+=8;return num;case (byte)'T':return true;case (byte)'F':return false;case (byte)'0':case (byte)'_':return null;case (byte)'@':uint r=Uint();return r<refs.Count?refs[(int)r]:null;case (byte)'o':var obj=new Dictionary<string,object>();refs.Add(obj);while(p<data.Length&&data[p]!='{'){var key=Value(depth+1) as string;if(key==null)throw new FormatException();obj[key]=Value(depth+1);}if(Byte()!='{')throw new FormatException();Uint();return obj;case (byte)'A':uint length=Uint();if(length>50000)throw new FormatException();var list=new List<object>();refs.Add(list);for(uint j=0;j<length;j++)list.Add(Value(depth+1));if(Byte()!='$')throw new FormatException();Uint();Uint();return list;default:throw new FormatException();}}
 public Dictionary<string,object> ObjectFields(){var obj=new Dictionary<string,object>();refs.Add(obj);try{if(Byte()!='o')return obj;while(p<data.Length&&data[p]!='{'){var key=Value(1) as string;if(key==null)break;obj[key]=Value(1);}}catch{}return obj;}
}
public static class LocalBlocks {
 const int Limit=64*1024*1024;
 static ulong Var(byte[] data,ref int p){ulong n=0;int shift=0;for(int i=0;i<10;i++){if(p>=data.Length)throw new FormatException();byte b=data[p++];n|=(ulong)(b&127)<<shift;if(b<128)return n;shift+=7;}throw new FormatException();}
 static byte[] Slice(byte[] bytes,int start,int length){if(start<0||length<0||start>bytes.Length-length)throw new FormatException();var result=new byte[length];Buffer.BlockCopy(bytes,start,result,0,length);return result;}
 public static IEnumerable<byte[]> Values(string file) {
  byte[] bytes;using(var f=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){if(f.Length>Limit)yield break;bytes=new byte[(int)f.Length];int n=0;while(n<bytes.Length){int read=f.Read(bytes,n,bytes.Length-n);if(read==0)break;n+=read;}}
  // WAL fragments are short and often hold complete serialized records; whole-file scanning is a fallback for partial batches.
  if(file.EndsWith(".log",StringComparison.OrdinalIgnoreCase)){yield return bytes;yield break;}
  if(bytes.Length<48)yield break;int footer=bytes.Length-48;ulong magic=BitConverter.ToUInt64(bytes,bytes.Length-8);if(magic!=0xdb4775248b80fb57UL)yield break;
  int position=footer;Var(bytes,ref position);Var(bytes,ref position);ulong indexOffset=Var(bytes,ref position),indexSize=Var(bytes,ref position);
  var index=Block(bytes,indexOffset,indexSize);foreach(var handle in Entries(index)){int hp=0;ulong offset=Var(handle,ref hp),size=Var(handle,ref hp);foreach(var value in Entries(Block(bytes,offset,size)))yield return value;}
 }
 static byte[] Block(byte[] bytes,ulong offset,ulong size){if(offset>(ulong)bytes.Length||size>Limit||size+5>(ulong)bytes.Length-offset)throw new FormatException();var block=Slice(bytes,(int)offset,(int)size);int type=bytes[(int)(offset+size)];return type==0?block:type==1?Unsnappy(block):throwFormat();}
 static byte[] throwFormat(){throw new FormatException();}
 static IEnumerable<byte[]> Entries(byte[] block){if(block.Length<4)yield break;uint restarts=BitConverter.ToUInt32(block,block.Length-4);if(restarts>(uint)(block.Length/4-1))yield break;int end=block.Length-4-checked((int)restarts)*4,p=0,keyLength=0;while(p<end){ulong shared=Var(block,ref p),unshared=Var(block,ref p),valueLength=Var(block,ref p);if(shared>(ulong)keyLength||unshared>Limit||valueLength>Limit||unshared+valueLength>(ulong)(end-p))throw new FormatException();keyLength=(int)(shared+unshared);p+=(int)unshared;yield return Slice(block,p,(int)valueLength);p+=(int)valueLength;}}
 public static byte[] Unsnappy(byte[] input){int p=0;int length=checked((int)Var(input,ref p));if(length<0||length>Limit)throw new FormatException();var output=new byte[length];int written=0;while(p<input.Length&&written<length){byte tag=input[p++];int type=tag&3,count,offset=0;if(type==0){int n=tag>>2;if(n<60)count=n+1;else{int b=n-59;if(p+b>input.Length||b>4)throw new FormatException();uint val=0;for(int j=0;j<b;j++)val|=(uint)input[p++]<<(j*8);count=checked((int)val+1);}if(count>length-written||count>input.Length-p)throw new FormatException();Buffer.BlockCopy(input,p,output,written,count);p+=count;written+=count;continue;}if(type==1){count=4+((tag>>2)&7);if(p>=input.Length)throw new FormatException();offset=((tag&224)<<3)|input[p++];}else{count=1+(tag>>2);int b=type==2?2:4;if(p+b>input.Length)throw new FormatException();uint val=0;for(int j=0;j<b;j++)val|=(uint)input[p++]<<(j*8);offset=checked((int)val);}if(offset<=0||offset>written||count>length-written)throw new FormatException();for(int j=0;j<count;j++)output[written+j]=output[written-offset+j];written+=count;}if(written!=length)throw new FormatException();return output;}
}
}
