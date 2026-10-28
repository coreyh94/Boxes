namespace DesktopBoxes;
internal static class PlacementTests
{
 internal static void Run(){
 var screen=new Rectangle(0,0,1000,800);var spacing=new Size(80,100);
 var icons=new[]{new Point(40,8),new Point(120,8),new Point(400,408)};
 var boxes=new[]{new Rectangle(0,0,192,288),new Rectangle(192,0,192,288)};
 var result=Placement.Arrange(icons,spacing,boxes,[screen])??throw new Exception("No plan");
 if(result[2]!=icons[2])throw new Exception("Uncovered icon moved");
 for(int i=0;i<result.Length;i++){
 var cell=Placement.Cell(result[i],spacing);
 if(boxes.Any(b=>b.IntersectsWith(cell))||result.Where((_,j)=>j!=i).Any(p=>Placement.Cell(p,spacing).IntersectsWith(cell)))throw new Exception("Overlap");
 }
 if(Placement.Arrange(icons,spacing,[screen],[screen])!=null)throw new Exception("Full screen accepted");
 var negative=new Rectangle(-1000,-800,1000,800);
 if(Placement.FindBox(new Size(384,288),[],[negative])?.Location!=negative.Location)throw new Exception("Negative monitor failed");
 if(Placement.FindBox(new Size(384,288),[screen],[screen])!=null)throw new Exception("Overlapping box accepted");
 var again=Placement.Arrange(result,spacing,boxes,[screen]);
 if(again==null||!again.SequenceEqual(result))throw new Exception("Placement not stable");
 // Monitor changes: left monitor unplugged, then plugged back in; resolution drop on the main monitor.
 var left=new Rectangle(-1920,0,1920,1032);var main=new Rectangle(0,0,1920,1032);
 var homes=new[]{new Rectangle(-1872,48,720,432),new Rectangle(48,48,720,432),new Rectangle(0,0,480,336)};
 var both=Placement.Fit(homes,[left,main]);
 if(both[0]!=homes[0]||both[1]!=homes[1])throw new Exception("Boxes left home with all monitors present");
 if(both[2] is not { } moved||moved.IntersectsWith(homes[1])||!main.Contains(moved))throw new Exception("Overlapping saved box not relocated");
 var unplugged=Placement.Fit(homes,[main]);
 if(unplugged[1]!=homes[1])throw new Exception("Box on remaining monitor moved");
 if(unplugged.Any(r=>r==null||!main.Contains(r.Value)))throw new Exception("Displaced box not placed on remaining monitor");
 if(unplugged.Where((r,i)=>unplugged.Where((_,j)=>j!=i).Any(o=>o!.Value.IntersectsWith(r!.Value))).Any())throw new Exception("Displaced boxes overlap");
 var replugged=Placement.Fit(homes,[left,main]);
 if(replugged[0]!=homes[0])throw new Exception("Box did not return home when monitor came back");
 var small=new Rectangle(0,0,1280,672);
 var lowRes=Placement.Fit([new Rectangle(48,48,1440,816)],[small]);
 if(lowRes[0] is not { } shrunk||!small.Contains(shrunk))throw new Exception("Oversized box not shrunk to fit lower resolution");
 if(Placement.Fit([homes[0],homes[1]],[new Rectangle(0,0,480,240)]).Count(r=>r!=null)!=1)throw new Exception("Expected one box hidden when space runs out");
 }
}
