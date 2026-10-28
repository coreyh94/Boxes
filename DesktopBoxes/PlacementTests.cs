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
 }
}
