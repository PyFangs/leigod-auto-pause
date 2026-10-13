using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace LeigodGuard {
static class WinAsync {
 public static TaskAwaiter<T> GetAwaiter<T>(this IAsyncOperation<T> op) { return Wait(op).GetAwaiter(); }
 public static TaskAwaiter<T> GetAwaiter<T,P>(this IAsyncOperationWithProgress<T,P> op) { return Wait(op).GetAwaiter(); }
 static async Task<T> Wait<T>(IAsyncOperation<T> op) {
  var clock=Stopwatch.StartNew();
  try { while(op.Status==AsyncStatus.Started) { if(clock.ElapsedMilliseconds>5000) {op.Cancel();throw new TimeoutException("Windows 文字识别超时");} await Task.Delay(15); } return op.GetResults(); } finally { op.Close(); }
 }
 static async Task<T> Wait<T,P>(IAsyncOperationWithProgress<T,P> op) {
  var clock=Stopwatch.StartNew();
  try { while(op.Status==AsyncStatus.Started) { if(clock.ElapsedMilliseconds>5000) {op.Cancel();throw new TimeoutException("Windows 图像解码超时");} await Task.Delay(15); } return op.GetResults(); } finally { op.Close(); }
 }
}
public class TextLine {
 public string Text;
 public double X,Y,W,H;
 public string Normalized { get { return Client.Normalize(Text); } }
}
public class Frame {
 public long Window;
 public int Width,Height;
 public List<TextLine> Lines=new List<TextLine>();
 public List<string> ActiveRows=new List<string>();
 public TextLine PauseButton,ResumeButton;
 public bool Minimized;
 public string Error;
 public DateTime CapturedUtc;
 public bool Active {get { return PauseButton!=null && ActiveRows.Count>0; }}
}
public static class Native {
 [StructLayout(LayoutKind.Sequential)] public struct RECT {public int Left,Top,Right,Bottom;}
 [StructLayout(LayoutKind.Sequential)] public struct POINT {public int X,Y;}
 public delegate bool EnumProc(IntPtr h,IntPtr l);
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p,IntPtr l);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h,StringBuilder s,int n);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out RECT r);
 [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
 [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h,IntPtr dc,uint flags);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int command);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
 [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h,uint f);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
 [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
 [DllImport("user32.dll",EntryPoint="OpenInputDesktop")] public static extern IntPtr InputDesktop(uint f,bool inherit,uint access);
 [DllImport("user32.dll")] public static extern bool CloseDesktop(IntPtr h);
 [DllImport("shcore.dll")] public static extern int SetProcessDpiAwareness(int value);
 [DllImport("user32.dll")] public static extern uint SendInput(uint n,INPUT[] inputs,int size);
 [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type;public MOUSEINPUT mi; }
 [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT {public int dx,dy;public uint mouseData,dwFlags,time;public UIntPtr extra;}
 [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr OpenProcess(uint access,bool inherit,uint id);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool QueryFullProcessImageName(IntPtr process,uint flags,StringBuilder path,ref uint size);
 [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
 [DllImport("advapi32.dll",SetLastError=true)] static extern bool OpenProcessToken(IntPtr h,uint access,out IntPtr token);
 [DllImport("advapi32.dll",SetLastError=true)] static extern bool GetTokenInformation(IntPtr token,int cls,out int info,int len,out int needed);
 public static string ProcessPath(uint id){var handle=OpenProcess(0x1000,false,id);if(handle==IntPtr.Zero)return null;try{var text=new StringBuilder(32768);uint length=(uint)text.Capacity;return QueryFullProcessImageName(handle,0,text,ref length)?text.ToString():null;}finally{CloseHandle(handle);}}
 public static bool Elevated(uint pid) {
  var h=OpenProcess(0x1000,false,pid);if(h==IntPtr.Zero)return false;
  IntPtr t=IntPtr.Zero;
  try {int v,n;return OpenProcessToken(h,8,out t) && GetTokenInformation(t,20,out v,4,out n) && v!=0;} finally {if(t!=IntPtr.Zero)CloseHandle(t);CloseHandle(h);}
 }
 public static bool DesktopReady() { var h=InputDesktop(0,false,0x100);if(h==IntPtr.Zero)return false;CloseDesktop(h);return true; }
 public static IntPtr FindClient() {
  var ids=new HashSet<uint>();foreach(var p in Process.GetProcessesByName("leigod")) {using(p){ids.Add((uint)p.Id);}}
  IntPtr best=IntPtr.Zero;long area=0;
  EnumWindows(delegate(IntPtr h,IntPtr unused){
   uint id;GetWindowThreadProcessId(h,out id);if(!ids.Contains(id))return true;
   var title=new StringBuilder(512);GetWindowText(h,title,512);if(title.ToString()!="雷神加速器")return true;
   RECT r;if(!GetWindowRect(h,out r))return true;long a=(long)(r.Right-r.Left)*(r.Bottom-r.Top);
   if(a>area){best=h;area=a;}return true;
  },IntPtr.Zero);return best;
 }
 public static void RestoreClient(IntPtr h) {
  uint pid;GetWindowThreadProcessId(h,out pid);
  // A tray-hidden Electron window must be shown by its own single-instance handler.
  // SW_RESTORE alone can leave Chromium's renderer hidden and the window black.
  string path;using(var process=Process.GetProcessById((int)pid))path=process.MainModule.FileName;
  if(!string.Equals(Path.GetFileName(path),"leigod.exe",StringComparison.OrdinalIgnoreCase))throw new Exception("目标不是雷神客户端，已取消恢复");
  using(var started=Process.Start(new ProcessStartInfo(path){UseShellExecute=true})){}
 }
 public static bool FullyVisible(IntPtr h,RECT r) {
  if(!IsWindowVisible(h)||IsIconic(h)||GetForegroundWindow()!=h)return false;
  foreach(var p in new[]{new POINT{X=r.Left+15,Y=r.Top+15},new POINT{X=r.Right-15,Y=r.Top+15},new POINT{X=r.Left+15,Y=r.Bottom-15},new POINT{X=r.Right-15,Y=r.Bottom-15},new POINT{X=(r.Left+r.Right)/2,Y=(r.Top+r.Bottom)/2}})
   if(GetAncestor(WindowFromPoint(p),2)!=h)return false;
  return true;
 }
 public static Bitmap Capture(IntPtr h,bool visibleOnly=false) {
  RECT r;if(!IsWindow(h)||!GetWindowRect(h,out r))throw new Exception("雷神窗口已关闭");
  if(IsIconic(h)||!IsWindowVisible(h))throw new Exception("雷神已隐藏到托盘，等待恢复后读取");
  int w=r.Right-r.Left,he=r.Bottom-r.Top;if(w<100||he<100||w>6000||he>4000)throw new Exception("雷神窗口尺寸无法识别");
  var bmp=new Bitmap(w,he,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
  try {
   // Visible capture avoids WM_PRINT forcing an Electron tray window into a black frame.
   if(FullyVisible(h,r)) {using(var g=Graphics.FromImage(bmp))g.CopyFromScreen(r.Left,r.Top,0,0,bmp.Size,CopyPixelOperation.SourceCopy);return bmp;}
   if(visibleOnly)throw new Exception("等待雷神主窗口显示；窗口不能被其他应用遮挡");
   if(IsIconic(h)||!IsWindowVisible(h))throw new Exception("雷神窗口刚隐藏到托盘，已跳过截图");
   bool printed;using(var g=Graphics.FromImage(bmp)){var dc=g.GetHdc();try {printed=PrintWindow(h,dc,2);}finally{g.ReleaseHdc(dc);}}
   bool black=true;for(int y=10;y<bmp.Height&&black;y+=Math.Max(1,bmp.Height/12))for(int x=10;x<bmp.Width&&black;x+=Math.Max(1,bmp.Width/12)){var c=bmp.GetPixel(x,y);if(c.R+c.G+c.B>30)black=false;}
   if(!printed||black) {
    // Unelevated monitors may read a fully visible window, but never an occluding app.
    if(!FullyVisible(h,r))throw new Exception("雷神窗口暂不可读取；保留已识别的监控记录");
    using(var g=Graphics.FromImage(bmp)){g.CopyFromScreen(r.Left,r.Top,0,0,bmp.Size,CopyPixelOperation.SourceCopy);}
   }
   return bmp;
  }catch {bmp.Dispose();throw;}
 }
 public static void Click(IntPtr h,Frame f,TextLine button) {
  RECT r;if(!IsWindow(h)||IsIconic(h)||GetForegroundWindow()!=h||!GetWindowRect(h,out r))throw new Exception("雷神窗口未处于前台，已取消点击");
  if(r.Right-r.Left!=f.Width||r.Bottom-r.Top!=f.Height)throw new Exception("雷神窗口尺寸发生变化，已取消点击");
  if(GetAsyncKeyState(1)<0||GetAsyncKeyState(2)<0||GetAsyncKeyState(16)<0||GetAsyncKeyState(17)<0||GetAsyncKeyState(18)<0)throw new Exception("检测到鼠标或修饰键正在操作，已取消点击");
  var point=new POINT{X=r.Left+(int)(button.X+button.W/2),Y=r.Top+(int)(button.Y+button.H/2)};
  if(GetAncestor(WindowFromPoint(point),2)!=h)throw new Exception("暂停按钮被其他窗口遮挡，已取消点击");
  POINT old;GetCursorPos(out old);SetCursorPos(point.X,point.Y);
  try {
   // Recheck ownership immediately before emitting a single atomic mouse click.
   if(GetForegroundWindow()!=h||GetAncestor(WindowFromPoint(point),2)!=h)throw new Exception("前台窗口发生变化，已取消点击");
   var input=new[]{new INPUT{type=0,mi=new MOUSEINPUT{dwFlags=2}},new INPUT{type=0,mi=new MOUSEINPUT{dwFlags=4}}};
   if(SendInput(2,input,Marshal.SizeOf(typeof(INPUT)))!=2)throw new Exception("Windows 阻止了点击，请使用管理员模式");
  }finally {SetCursorPos(old.X,old.Y);}
 }
}
public static class Client {
 public static string Normalize(string s) {return new string((s??"").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();}
 public static bool Matches(string row,GameBinding game) {var n=Normalize(row);return game.Aliases.Any(a=>Normalize(a).Length>=2 && n.Contains(Normalize(a)));}
 public static async Task<Frame> Scan(string saveImage=null,bool visibleOnly=false) {return await ScanWindow(Native.FindClient(),saveImage,visibleOnly);}
 public static async Task<Frame> ScanWindow(IntPtr h,string saveImage=null,bool visibleOnly=false) {
  var f=new Frame{CapturedUtc=DateTime.UtcNow};
  try {
   f.Window=h.ToInt64();if(h==IntPtr.Zero){f.Error="等待雷神启动";return f;}
   f.Minimized=Native.IsIconic(h)||!Native.IsWindowVisible(h);if(f.Minimized){f.Error="雷神已最小化或隐藏到托盘；保留已识别的监控记录";return f;}
   using(var bmp=Native.Capture(h,visibleOnly)) {
    f.Width=bmp.Width;f.Height=bmp.Height;if(saveImage!=null)bmp.Save(saveImage,ImageFormat.Png);
    int top=Math.Max(50,(int)(bmp.Height*.15));
    f.Lines.AddRange(await ReadRegion(bmp,new Rectangle(0,0,bmp.Width,top)));
    f.Lines.AddRange(await ReadRegion(bmp,new Rectangle(0,top,(int)(bmp.Width*.23),bmp.Height-top)));
   }
   IdentifyButtons(f);
   var marks=f.Lines.Where(l=>l.X<f.Width*.23 && l.Y>f.Height*.18 && l.Normalized.Contains("加速中")).ToList();
   foreach(var m in marks) {
    var nearby=f.Lines.Where(l=>l.X<f.Width*.23 && l.Y>=m.Y-Math.Max(36,f.Height*.04) && l.Y<=m.Y+m.H/2).OrderBy(l=>l.Y).ToList();
    var row=string.Join(" ",nearby.Select(l=>l.Text));if(row.Length>0&&!f.ActiveRows.Contains(row))f.ActiveRows.Add(row);
   }
   if(f.PauseButton==null&&f.ResumeButton==null)f.Error="未识别到时长按钮；请打开雷神主窗口";
  }catch(Exception e){f.Error=e.Message;}
 return f;
 }
 public static async Task<Frame> WaitForReady(Func<Task<Frame>> scan,Action recheck,Func<Task> wait,bool requireGame,int attempts=16) {
  Frame last=null;var elapsed=Stopwatch.StartNew();
  for(int i=0;i<attempts&&elapsed.Elapsed.TotalSeconds<10;i++) {
   recheck();await wait();recheck();last=await scan();recheck();
   if(last.ResumeButton!=null||(last.PauseButton!=null&&(!requireGame||last.Active)))return last;
  }
  throw new Exception("雷神界面未能从托盘恢复，尚未点击暂停。\n请双击雷神托盘图标打开主窗口，再从监控托盘菜单选择“暂停雷神时长…”。\n"+(last==null?"":last.Error));
 }
 public static async Task<Frame> RestoreAndRead(bool requireGame,Action recheck,bool visibleOnly=true) {
  recheck();var h=Native.FindClient();if(h==IntPtr.Zero)throw new Exception("雷神已关闭");
  Native.RestoreClient(h);
  return await WaitForReady(async()=>{
   var current=Native.FindClient();if(current!=h)throw new Exception("雷神窗口发生变化，已取消暂停");
   if(Native.IsWindowVisible(h)&&!Native.IsIconic(h))Native.SetForegroundWindow(h);
   return await ScanWindow(h,null,visibleOnly);
  },recheck,()=>Task.Delay(400),requireGame);
 }
 public static void IdentifyButtons(Frame f) {
  // Only accept the billing toggle in the top-right toolbar, not game launch buttons.
  f.PauseButton=f.Lines.FirstOrDefault(l=>l.X>f.Width*.60 && l.Y<f.Height*.15 && (l.Normalized=="暂停时长"||l.Normalized=="暂停时间"));
  f.ResumeButton=f.Lines.FirstOrDefault(l=>l.X>f.Width*.60 && l.Y<f.Height*.15 && (l.Normalized=="开启时长"||l.Normalized=="恢复时长"||l.Normalized=="恢复时间"||l.Normalized=="开始时长"));
 }
 static async Task<List<TextLine>> ReadRegion(Bitmap source,Rectangle rect) {
  double scale=Math.Min(2.0,Math.Min((double)OcrEngine.MaxImageDimension/rect.Width,(double)OcrEngine.MaxImageDimension/rect.Height));
  using(var region=source.Clone(rect,System.Drawing.Imaging.PixelFormat.Format32bppArgb))using(var scaled=new Bitmap((int)(rect.Width*scale),(int)(rect.Height*scale))) {
   using(var g=Graphics.FromImage(scaled)){g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;g.DrawImage(region,0,0,scaled.Width,scaled.Height);}
   var lines=await Read(scaled,scale);foreach(var line in lines){line.X+=rect.X;line.Y+=rect.Y;}return lines;
  }
 }
 static async Task<List<TextLine>> Read(Bitmap bmp,double scale) {
  var engine=OcrEngine.TryCreateFromLanguage(new Language("zh-Hans-CN"))??OcrEngine.TryCreateFromUserProfileLanguages();
  if(engine==null)throw new Exception("Windows 缺少文字识别语言，请安装中文（简体）语言包");
  using(var ms=new MemoryStream())using(var stream=new InMemoryRandomAccessStream()) {
   bmp.Save(ms,ImageFormat.Png);var writer=new DataWriter(stream);writer.WriteBytes(ms.ToArray());await writer.StoreAsync();writer.DetachStream();writer.Dispose();stream.Seek(0);
   var decoder=await BitmapDecoder.CreateAsync(stream);
   using(var software=await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied)) {
    var result=await engine.RecognizeAsync(software);var lines=new List<TextLine>();
    foreach(var line in result.Lines) {
     if(line.Words.Count==0)continue;
     // Split merged OCR lines into spatial clusters so a top-bar counter cannot become part of the pause button.
     var cluster=new List<OcrWord>();double previous=0;
     foreach(var word in line.Words.OrderBy(w=>w.BoundingRect.X)) {
      if(cluster.Count>0 && word.BoundingRect.X-previous>Math.Max(24*scale,word.BoundingRect.Height*1.8)) {lines.Add(MakeLine(cluster,scale));cluster.Clear();}
      cluster.Add(word);previous=word.BoundingRect.X+word.BoundingRect.Width;
     }
     if(cluster.Count>0)lines.Add(MakeLine(cluster,scale));
    }
    return lines;
   }
  }
 }
 static TextLine MakeLine(List<OcrWord> words,double s) {
  double x=words.Min(w=>w.BoundingRect.X),y=words.Min(w=>w.BoundingRect.Y);
  return new TextLine{Text=string.Join("",words.Select(w=>w.Text)),X=x/s,Y=y/s,W=(words.Max(w=>w.BoundingRect.X+w.BoundingRect.Width)-x)/s,H=(words.Max(w=>w.BoundingRect.Y+w.BoundingRect.Height)-y)/s};
 }
}
}
