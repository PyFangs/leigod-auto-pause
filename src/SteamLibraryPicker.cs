using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LeigodGuard {
public class SteamLibraryPicker:Form {
 public readonly TextBox Search=new TextBox();
 public readonly TextBox NameEntry=new TextBox();
 public readonly TextBox DirectoryEntry=new TextBox();
 public readonly ListView GameList=new ListView();
 public GameBinding SelectedGame {get;private set;}
 public string SelectedExecutable {get;private set;}
 public string SelectedName {get;private set;}
 public string[] AddedLibraries {get{return addedLibraries.ToArray();}}
 public string SuggestedDirectory {get{return DefaultBrowseDirectory(selected!=null?selected.InstallDirectory:!string.IsNullOrEmpty(file)?Path.GetDirectoryName(file):null,settings.SteamLibraries??new string[0]);}}
 readonly Settings settings;
 readonly Func<List<GameBinding>> loader;
 readonly List<string> addedLibraries=new List<string>();
 List<GameBinding> allGames=new List<GameBinding>();
 GameBinding selected;
 string file;
 Label information;
 Button add;
 public SteamLibraryPicker(Settings settings,Func<List<GameBinding>> loader=null) {
  SuspendLayout();this.settings=settings;this.loader=loader;
  Text="添加游戏";Font=new Font("Microsoft YaHei UI",10);ClientSize=new Size(640,475);MinimumSize=new Size(610,460);MaximizeBox=false;MinimizeBox=false;ShowInTaskbar=false;StartPosition=FormStartPosition.CenterParent;BackColor=Color.White;AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;KeyPreview=true;
  var root=new Panel{Dock=DockStyle.Fill,Padding=new Padding(20)};Controls.Add(root);
  GameList.Dock=DockStyle.Fill;GameList.View=View.Details;GameList.FullRowSelect=true;GameList.HideSelection=false;GameList.MultiSelect=false;GameList.Columns.Add("Steam 游戏",440);GameList.Columns.Add("监控状态",120);root.Controls.Add(GameList);
  var details=new TableLayoutPanel{Dock=DockStyle.Bottom,Height=120,ColumnCount=2,RowCount=3,Padding=new Padding(0,12,0,0)};details.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,112));details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));details.RowStyles.Add(new RowStyle(SizeType.Absolute,34));details.RowStyles.Add(new RowStyle(SizeType.Absolute,34));details.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.Controls.Add(details);
  details.Controls.Add(new Label{Text="雷神中的名称",AutoSize=true,Margin=new Padding(0,6,0,0)},0,0);NameEntry.Dock=DockStyle.Fill;details.Controls.Add(NameEntry,1,0);
  details.Controls.Add(new Label{Text="安装位置",AutoSize=true,Margin=new Padding(0,6,0,0)},0,1);DirectoryEntry.Dock=DockStyle.Fill;DirectoryEntry.ReadOnly=true;details.Controls.Add(DirectoryEntry,1,1);
  var folder=new LinkLabel{Text="打开游戏文件夹",AutoSize=true,Margin=new Padding(0,6,0,0)};details.Controls.Add(folder,1,2);folder.LinkClicked+=(a,b)=>{var directory=SuggestedDirectory;if(directory!=null&&Directory.Exists(directory))Process.Start(new ProcessStartInfo(directory){UseShellExecute=true});};
  var footer=new Panel{Dock=DockStyle.Bottom,Height=44};root.Controls.Add(footer);
  var browse=new Button{Text="手动添加游戏",Size=new Size(145,34),Location=new Point(0,8)};footer.Controls.Add(browse);browse.Click+=(a,b)=>BrowseExecutable();
  var cancel=new Button{Text="取消",Size=new Size(80,34),Location=new Point(370,8)};footer.Controls.Add(cancel);cancel.Click+=(a,b)=>{DialogResult=DialogResult.Cancel;Close();};CancelButton=cancel;
  add=new Button{Text="添加游戏",Size=new Size(120,34),Location=new Point(464,8),Enabled=false};footer.Controls.Add(add);add.Click+=(a,b)=>ConfirmSelection();footer.Resize+=(a,b)=>{add.Left=footer.ClientSize.Width-add.Width;cancel.Left=add.Left-12-cancel.Width;};
  var infoRow=new Panel{Dock=DockStyle.Top,Height=36};root.Controls.Add(infoRow);information=new Label{Text="正在读取 Steam 游戏库…",AutoSize=true,Location=new Point(0,6),ForeColor=Color.DimGray};infoRow.Controls.Add(information);
  var searchRow=new TableLayoutPanel{Dock=DockStyle.Top,Height=40,ColumnCount=2};searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,90));searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));root.Controls.Add(searchRow);searchRow.Controls.Add(new Label{Text="搜索游戏",AutoSize=true,Margin=new Padding(0,6,0,0)},0,0);Search.Dock=DockStyle.Fill;searchRow.Controls.Add(Search,1,0);
  Search.TextChanged+=(a,b)=>FilterGames();NameEntry.TextChanged+=(a,b)=>UpdateAddButton();GameList.SelectedIndexChanged+=(a,b)=>SelectRow();GameList.DoubleClick+=(a,b)=>ConfirmSelection();GameList.Resize+=(a,b)=>{if(GameList.ClientSize.Width>150){GameList.Columns[1].Width=130;GameList.Columns[0].Width=Math.Max(200,GameList.ClientSize.Width-154);}};
  KeyDown+=(a,b)=>{if(b.Control&&b.KeyCode==Keys.F){Search.Focus();Search.SelectAll();b.SuppressKeyPress=true;}};ResumeLayout(true);
 }
 protected override async void OnShown(EventArgs e){base.OnShown(e);await Reload();}
 async Task Reload(){information.Text="正在读取 Steam 游戏库…";try{var result=await Task.Run(()=>loader!=null?loader():Discovery.Scan(Discovery.SteamRoots().Concat(settings.SteamLibraries??new string[0]).Concat(addedLibraries),Discovery.CacheFolders()).Games);if(IsDisposed)return;SetGames(result);}catch{if(!IsDisposed)information.Text="未能读取 Steam 库，可手动添加游戏。";}}
 public void SetGames(IEnumerable<GameBinding> games){allGames=games.Where(g=>!string.IsNullOrEmpty(g.SteamAppId)&&!string.IsNullOrEmpty(g.InstallDirectory)&&Directory.Exists(g.InstallDirectory)).GroupBy(g=>g.SteamAppId).Select(g=>g.First()).OrderBy(g=>g.Name,StringComparer.CurrentCultureIgnoreCase).ToList();FilterGames();}
 void FilterGames(){string query=Search.Text.Trim();GameList.BeginUpdate();try{GameList.Items.Clear();foreach(var game in allGames.Where(g=>query.Length==0||new[]{g.Name,g.SteamAppId,g.InstallDirectory}.Concat(g.Aliases).Any(s=>s!=null&&s.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0))){var existing=settings.Games.FirstOrDefault(g=>g.SteamAppId==game.SteamAppId);var item=new ListViewItem(game.Name){Tag=game};item.SubItems.Add(existing==null?"可添加":existing.Enabled?"已添加":"已关闭");GameList.Items.Add(item);}information.Text=GameList.Items.Count==0?"没有匹配的游戏，可手动添加。":"找到 "+GameList.Items.Count+" 款已安装游戏";if(GameList.Items.Count>0)GameList.Items[0].Selected=true;else{selected=null;file=null;NameEntry.Text="";DirectoryEntry.Text="";UpdateAddButton();}}finally{GameList.EndUpdate();}}
 void SelectRow(){if(GameList.SelectedItems.Count==0)return;selected=(GameBinding)GameList.SelectedItems[0].Tag;file=null;var existing=settings.Games.FirstOrDefault(g=>g.SteamAppId==selected.SteamAppId);NameEntry.Text=existing==null?selected.Name:existing.Name;DirectoryEntry.Text=selected.InstallDirectory;UpdateAddButton();}
 void UpdateAddButton(){add.Enabled=NameEntry.Text.Trim().Length>=2&&(selected!=null||file!=null);}
 public static string ResolveLibraryRoot(string path){try{var full=Path.GetFullPath(path);var root=full.Length>Path.GetPathRoot(full).Length?full.TrimEnd(Path.DirectorySeparatorChar):full;if(Path.GetFileName(root).Equals("common",StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(Path.GetDirectoryName(root)).Equals("steamapps",StringComparison.OrdinalIgnoreCase))root=Path.GetDirectoryName(Path.GetDirectoryName(root));else if(Path.GetFileName(root).Equals("steamapps",StringComparison.OrdinalIgnoreCase))root=Path.GetDirectoryName(root);return Directory.Exists(Path.Combine(root,"steamapps"))?root:null;}catch{return null;}}
 public static string DefaultBrowseDirectory(string preferred,IEnumerable<string> libraries){if(!string.IsNullOrEmpty(preferred)&&Directory.Exists(preferred))return preferred;foreach(var root in libraries.Concat(Discovery.SteamRoots()).Distinct(StringComparer.OrdinalIgnoreCase)){var common=Path.Combine(root,"steamapps","common");if(Directory.Exists(common))return common;}return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);}
 void BrowseExecutable(){using(var dialog=new OpenFileDialog{Filter="游戏程序 (*.exe)|*.exe",Title="手动添加游戏",InitialDirectory=SuggestedDirectory}){if(dialog.ShowDialog(this)!=DialogResult.OK)return;if(Discovery.Auxiliary(Path.GetFileName(dialog.FileName))){MessageBox.Show(this,"请选择游戏本身的程序。","添加游戏");return;}var game=allGames.FirstOrDefault(g=>Discovery.Inside(dialog.FileName,g.InstallDirectory));file=dialog.FileName;selected=game;DirectoryEntry.Text=Path.GetDirectoryName(file);if(game==null){foreach(var item in GameList.SelectedItems.Cast<ListViewItem>().ToArray())item.Selected=false;string name=Path.GetFileNameWithoutExtension(file);try{var description=FileVersionInfo.GetVersionInfo(file).FileDescription;if(!string.IsNullOrWhiteSpace(description)&&description.Length<80)name=description;}catch{}NameEntry.Text=name;}else{NameEntry.Text=(settings.Games.FirstOrDefault(g=>g.SteamAppId==game.SteamAppId)??game).Name;}var parent=Path.GetDirectoryName(file);for(int depth=0;parent!=null&&depth<16;depth++,parent=Path.GetDirectoryName(parent)){if(!string.Equals(Path.GetFileName(parent),"steamapps",StringComparison.OrdinalIgnoreCase))continue;var root=ResolveLibraryRoot(parent);if(root!=null&&!addedLibraries.Contains(root,StringComparer.OrdinalIgnoreCase))addedLibraries.Add(root);break;}information.Text="已选择程序："+Path.GetFileName(file);UpdateAddButton();}}
 public bool ConfirmSelection(){var name=NameEntry.Text.Trim();if(name.Length<2||(selected==null&&file==null))return false;if(selected!=null&&!Directory.Exists(selected.InstallDirectory)){information.Text="游戏目录已不存在，请手动添加游戏。";return false;}if(file!=null&&!File.Exists(file)){information.Text="游戏程序已不存在，请重新选择。";return false;}SelectedGame=selected;SelectedExecutable=file;SelectedName=name;DialogResult=DialogResult.OK;Close();return true;}
}
}
