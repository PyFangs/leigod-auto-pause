using LeigodGuard;
class TestSettings:Settings {
 public TestSettings(){AutoDiscover=false;Games.Add(new GameBinding{Name="DOTA2",Aliases=new[]{"DOTA2","DOTA 2","刀塔2"},Processes=new[]{"dota2.exe"},Enabled=true});}
}
