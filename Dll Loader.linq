<Query Kind="Statements">
  <NuGetReference>Newtonsoft.Json</NuGetReference>
  <NuGetReference>reactiveui</NuGetReference>
  <NuGetReference>Splat</NuGetReference>
</Query>

var path = Path.Combine(Path.GetDirectoryName(Util.CurrentQueryPath), @"lib\XyEF.Automation.dll");
var assembly = Assembly.LoadFile(path);
var type = assembly.GetExportedTypes().Single(x => x.Name == "Program");

// 按上边的绿色按钮或F5来运行
// 要停止本程序的话 按上面的红色按钮是不够的
// 要按Shift+F5两次 一次停止主进程 第二次停止所有线程
type.GetMethod("Main").Invoke(null, null);

// 如果出现 软件激活失败 
// 记得复制机器码给我 别给我发截图!!