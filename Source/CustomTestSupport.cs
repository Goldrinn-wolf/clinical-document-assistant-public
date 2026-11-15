using System; using System.IO; using System.Collections.Generic; using System.Text; using System.Windows; using System.Windows.Media; using System.Windows.Media.Imaging; using System.Windows.Threading;
namespace SuiteCustomization
{
 public static class TestSupport
 {
  public static List<string> Results=new List<string>();
  public static void Check(bool ok,string name) { if(!ok)throw new Exception("FAIL "+name); Results.Add("PASS "+name); }
  public static void Finish(string dir) { Results.Add("TOTAL "+Results.Count+" custom checks passed");File.WriteAllLines(Path.Combine(dir,"custom-tests.txt"),Results,new UTF8Encoding(true)); }
  public static void Error(string dir,Exception ex) { Results.Add(ex.ToString());File.WriteAllLines(Path.Combine(dir,"custom-tests.txt"),Results,new UTF8Encoding(true));Environment.ExitCode=1; }
  public static IEnumerable<T> Elements<T>(DependencyObject root) where T:DependencyObject {for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T)yield return (T)child;foreach(var nested in Elements<T>(child))yield return nested;}}
  public static void Capture(Window window,string path)
  {
   window.Show();window.UpdateLayout();window.Dispatcher.Invoke(new Action(delegate{}),DispatcherPriority.ApplicationIdle);FrameworkElement content=(FrameworkElement)window.Content; var rect=new Rect(0,0,content.ActualWidth,content.ActualHeight);var visual=new DrawingVisual();using(var dc=visual.RenderOpen()) { dc.DrawRectangle(window.Background??Brushes.White,null,rect);dc.DrawRectangle(new VisualBrush(content),null,rect); }var bmp=new RenderTargetBitmap((int)Math.Ceiling(rect.Width),(int)Math.Ceiling(rect.Height),96,96,PixelFormats.Pbgra32);bmp.Render(visual);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));using(Stream stream=File.Create(path))png.Save(stream);
  }
 }
}
