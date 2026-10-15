using System.Text.Json;
using Avalonia;
using Avalonia.Media;

namespace Tizi.Desktop;

internal static class BrandGeometry
{
    private static readonly (Point[][] Lines,double Stroke) Source=Load();
    private static (Point[][],double) Load()
    {
        using var stream=typeof(BrandGeometry).Assembly.GetManifestResourceStream("Tizi.Desktop.ConnectionLadder.geometry.json")!;
        using var document=JsonDocument.Parse(stream);
        return(document.RootElement.GetProperty("curves").EnumerateArray().Select(c=>c.EnumerateArray().Select(p=>new Point(p[0].GetDouble(),p[1].GetDouble())).ToArray()).ToArray(),
            document.RootElement.GetProperty("strokeWidth").GetDouble());
    }
    internal static (Point[][] Lines,double Stroke) Lines(string name,int height)
    {
        var points=Source.Lines.SelectMany(c=>c).ToArray();var cx=(points.Min(p=>p.X)+points.Max(p=>p.X))/2;var cy=(points.Min(p=>p.Y)+points.Max(p=>p.Y))/2;
        var scale=name=="BrandMark"?height/(points.Max(p=>p.X)-points.Min(p=>p.X)+Source.Stroke):height/32d;
        Point Map(Point p)=>name=="BrandMark"?new((cy-p.Y)*scale,(p.X-cx)*scale):new((p.X-48)*scale,(p.Y-18)*scale);
        return(Source.Lines.Select(line=>line.Select(Map).ToArray()).ToArray(),Source.Stroke*scale);
    }
    internal static StreamGeometry Path(IEnumerable<Point[]> lines)
    {
        var geometry=new StreamGeometry();using(var path=geometry.Open())foreach(var line in lines)
        {path.BeginFigure(line[0],false);path.LineTo(line[1]);path.EndFigure(false);}return geometry;
    }
}
