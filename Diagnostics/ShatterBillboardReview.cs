using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PianoPath;

/// <summary>Separate geometry experiment: V1 material on both sides, old velocity-plane vs Stage-clock tangent billboard.</summary>
internal static class ShatterBillboardReview
{
    private const int Width=640,Height=360;
    private static readonly JsonSerializerOptions Json=new(){IncludeFields=true};
    private sealed record Record(string Id,string Palette,string View,int Direction,int Pitch,int Sample,string Variant,string File,
        string PixelSha256,string RawHash,string SelectedHash,string ColourHash,string GeometryHash,string UnchangedHash,int Count,
        double MaxCornerError,long Signal,int VisiblePixels);
    private sealed record Frame(byte[] Pixels,Record Record);
    private static readonly ArNoteDirection[] Directions=[new(),new(45,-45),new(90,90),new(135,60)];
    private static readonly int[] Samples=[2,10,26,89];

    internal static int Render(string output)
    {
        Directory.CreateDirectory(output);
        var root=Path.GetFullPath(Path.Combine(PrismFlowVisualReview.FindSamples(),"..",".."));
        using var priorStream=new GZipStream(File.OpenRead(Path.Combine(root,"docs/evidence/shatter-art-v1/manifest.json.gz")),CompressionMode.Decompress);
        using var prior=JsonDocument.Parse(priorStream);
        var history=prior.RootElement.GetProperty("captures").EnumerateArray().Where(c=>c.GetProperty("Variant").GetString()=="art")
            .ToDictionary(c=>c.GetProperty("Id").GetString()!,c=>c.GetProperty("PixelSha256").GetString()!);
        var previous=KeyboardLayout.Active;KeyboardLayout.Use(KeyboardLayout.Standard88);
        try
        {
            using var renderer=GpuStageRenderer.Create(forceWarp:true);
            if(!renderer.IsWarp)throw new InvalidDataException("Billboard review requires native WARP.");
            renderer.SuppressStageKeyLightingForReview=true;renderer.SuppressArKeyTintForReview=renderer.SuppressArKeySpillForReview=true;
            var camera=PrismFlowVisualReview.ReadSyntheticCamera(Path.Combine(PrismFlowVisualReview.FindSamples(),"ar-keyboard-topdown.png"));
            var captures=new List<Record>();var motions=new List<object>();var lineage=0;
            foreach(var palette in new[]{"prism","indigo"})
            foreach(var view in HeldParticleControlPlan.Views)
            foreach(var direction in Enumerable.Range(0,view.OverCamera?4:1))
            foreach(var pitch in new[]{60,61})
            foreach(var sample in Samples)
            {
                var id=$"{palette}-{view.Id}-d{direction}-p{pitch}-f{sample}";
                var off=Draw(renderer,camera,view,palette,pitch,sample,60,direction,"off",id,false);
                var before=Draw(renderer,camera,view,palette,pitch,sample,60,direction,"before",id,false,offPixels:off.Pixels);
                var after=Draw(renderer,camera,view,palette,pitch,sample,60,direction,"after",id,false,offPixels:off.Pixels);
                CheckPair(before.Record,after.Record,view.OverCamera,sample!=89);
                if(sample!=89 && after.Record.Signal==0)throw new InvalidDataException("Aligned active Shatter is raster-empty: "+id);
                if(sample==89 && (before.Record.PixelSha256!=off.Record.PixelSha256 || after.Record.PixelSha256!=off.Record.PixelSha256))
                    throw new InvalidDataException("Expired billboard changed the off scene: "+id);
                var originalDirection=view.Id=="ar-oblique"?1:view.Id=="ar-near-overhead"?2:0;
                if(direction==originalDirection)
                {
                    var phase=sample==2?"burst":sample==10?"held":sample==26?"released":"expired";
                    if(before.Record.PixelSha256!=history[$"{palette}-{view.Id}-p{pitch}-{phase}"])
                        throw new InvalidDataException("Before geometry no longer reproduces frozen Shatter V1: "+id);
                    lineage++;
                }
                foreach(var frame in new[]{off,before,after})
                {
                    Write(Path.Combine(output,frame.Record.File),frame.Pixels,Width,Height);captures.Add(frame.Record);
                }
            }
            foreach(var view in new[]{HeldParticleControlPlan.Views[1],HeldParticleControlPlan.Views[3]})
            foreach(var soft in new[]{false,true})
            {
                var phase=soft?"held":"burst";var id=view.Id+"-"+phase;var direction=view.Id=="ar-reference"?0:2;
                var pitch=view.Id=="ar-reference"?60:61;
                var tracks=new Dictionary<string,List<Frame>>();
                foreach(var variant in new[]{"off","before","after"})
                {
                    var frames=new List<Frame>();
                    for(var f=0;f<60;f++)frames.Add(Draw(renderer,camera,view,"prism",pitch,f,30,direction,variant,id,true,soft,variant=="off"?null:tracks["off"][f].Pixels));
                    tracks.Add(variant,frames);
                }
                int x0=Width,y0=Height,x1=-1,y1=-1;
                for(var f=0;f<60;f++)
                {
                    CheckPair(tracks["before"][f].Record,tracks["after"][f].Record,true,tracks["after"][f].Record.Count>0);
                    foreach(var variant in new[]{"before","after"})
                    {
                        var pixels=tracks[variant][f].Pixels;var off=tracks["off"][f].Pixels;
                        for(var i=0;i<pixels.Length;i+=4)
                            if(pixels[i]!=off[i] || pixels[i+1]!=off[i+1] || pixels[i+2]!=off[i+2])
                            {var x=i/4%Width;var y=i/4/Width;x0=Math.Min(x0,x);x1=Math.Max(x1,x);y0=Math.Min(y0,y);y1=Math.Max(y1,y);}
                    }
                }
                if(x1<0)throw new InvalidDataException("No billboard motion signal.");
                x0=Math.Max(0,x0-4);y0=Math.Max(0,y0-4);x1=Math.Min(Width-1,x1+4);y1=Math.Min(Height-1,y1+4);
                var w=x1-x0+1;var h=y1-y0+1;var records=new List<object>();
                foreach(var variant in new[]{"off","before","after"})
                {
                    var hashes=new List<string>();var rows=new List<Record>();
                    for(var f=0;f<60;f++)
                    {
                        var pixels=Crop(tracks[variant][f].Pixels,x0,y0,w,h);var file=$"motion/{id}/{variant}/{f:D3}.png";
                        Write(Path.Combine(output,file),pixels,w,h);hashes.Add(Hash(pixels));rows.Add(tracks[variant][f].Record);
                    }
                    records.Add(new{variant,hashes,frames=rows});
                }
                motions.Add(new{Id=id,view=view.Id,pitch,direction,phase,fps=30,count=60,pedalFrame=12,velocity=soft?.45:.9,
                    crop=new{X=x0,Y=y0,Width=w,Height=h},tracks=records});
            }
            string[] sources=["Gpu/StageShaders.hlsl","Gpu/SparkParticleAppearance.cs","Gpu/ShatterParticleAppearance.cs","Gpu/GpuLook.cs","Gpu/GpuStageSimulation.cs",
                "Gpu/GpuStageSimulation.Effects.cs","Gpu/GpuStageRenderer.cs","Gpu/GpuArLayer.cs","Gpu/GpuArLayer.Effects.cs","Gpu/GpuStageFeed.cs","Gpu/GpuRenderLoop.cs",
                "Camera/ArParticleBillboard.cs","Camera/Ar3DNoteProjection.cs","Camera/ArCameraPose.cs","Stage/PianoStage.cs","Diagnostics/KeyParticleFixtures.cs",
                "Diagnostics/HeldParticleControlPlan.cs","Diagnostics/ShatterParticleArtReview.cs","Diagnostics/ShatterBillboardReview.cs","Diagnostics/AuxParticleFixtures.cs",
                "Diagnostics/AuxParticleTimeline.cs","docs/samples/ar-keyboard-topdown.png"];
            if(captures.Count!=624 || lineage!=64)throw new InvalidDataException("Incomplete Shatter billboard matrix or frozen lineage.");
            File.WriteAllText(Path.Combine(output,"manifest.json"),JsonSerializer.Serialize(new{schema=1,family="shatter-billboard",reviewOnly=true,
                sourceRevision=Environment.GetEnvironmentVariable("GITHUB_SHA")??"workspace",adapter=renderer.AdapterName,width=Width,height=Height,
                seed=KeyParticleFixtures.Seed,staticFps=60,samples=Samples,directions=Directions,staticCount=captures.Count,lineage,
                input="Single f0 impact only; static pedal f21, motion pedal f12; held track suffix means soft .45, never held/release emission",
                sourceHashes=sources.ToDictionary(p=>p,p=>Hash(p.EndsWith(".png")?File.ReadAllBytes(Path.Combine(root,p)):
                    Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(root,p)).Replace("\r\n","\n")))),captures,motions},Json));
            renderer.ObserveParticleStateForReview=null;renderer.ObserveStageSpriteForReview=null;renderer.ObserveArQuadForReview=null;renderer.ObserveArParticleForReview=null;
            return captures.Count;
        }
        finally{KeyboardLayout.Use(previous);}
    }

    private static Frame Draw(GpuStageRenderer renderer,byte[] camera,HeldParticleControlPlan.View view,string palette,int pitch,int sample,int fps,
        int direction,string variant,string id,bool motion,bool soft=false,byte[]? offPixels=null)
    {
        // Reuse the exact V1 source/input pipeline, including native packet observers. Both sides use V1 pigment/facet/glint.
        var frame=ShatterParticleArtReview.Draw(renderer,view,camera,palette,pitch,motion && soft?"held":"burst",variant=="off"?"off":"art",sample,fps,
            motion:motion,billboard:variant=="after",directionOverride:Directions[direction]);
        var a=frame.Audit;var cornerError=0.0;
        if(variant=="after" && view.OverCamera)cornerError=CheckActualCorners(a);
        if(variant=="after" && !view.OverCamera)
        {
            var bodies=a.Sprites.Where(q=>q.PosSize.W==31).ToArray();
            if(bodies.Length!=a.Raw.Length)throw new InvalidDataException("Stage body identity missing.");
            for(var i=0;i<bodies.Length;i++)
            {
                var angle=a.Raw[i].Phase+a.Raw[i].Age*7.3f;
                if(Vector2.Distance(new(bodies[i].Dir.X,bodies[i].Dir.Y),new(MathF.Cos(angle),MathF.Sin(angle)))>1e-6)
                    throw new InvalidDataException("Actual Stage tumble does not follow shared age/phase clock.");
            }
        }
        var unchanged=view.OverCamera?Hash(JsonSerializer.SerializeToUtf8Bytes(a.Quads.Select(q=>q.Misc.Z==31?
            new GpuArQuad{Color=q.Color,Misc=q.Misc}:q).ToArray(),Json)):Hash(JsonSerializer.SerializeToUtf8Bytes(a.Sprites,Json));
        var geometry=Hash(JsonSerializer.SerializeToUtf8Bytes(a.Quads.Select(q=>new{q.A,q.B,q.W}).ToArray(),Json));
        var selected=Hash(JsonSerializer.SerializeToUtf8Bytes(view.OverCamera?a.Selected:a.Raw,Json));
        // Off pixels are rendered independently, never inferred from masks or replaced with synthetic backgrounds.
        long signal=0;var visible=0;
        if(variant!="off")
        {
            offPixels??=ShatterParticleArtReview.Draw(renderer,view,camera,palette,pitch,motion && soft?"held":"burst","off",sample,fps,
                motion:motion,directionOverride:Directions[direction]).Pixels;
            for(var i=0;i<frame.Pixels.Length;i+=4)
            {
                var d=Math.Max(Math.Abs(frame.Pixels[i]-offPixels[i]),Math.Max(Math.Abs(frame.Pixels[i+1]-offPixels[i+1]),Math.Abs(frame.Pixels[i+2]-offPixels[i+2])));
                signal+=Math.Abs(frame.Pixels[i]-offPixels[i])+Math.Abs(frame.Pixels[i+1]-offPixels[i+1])+Math.Abs(frame.Pixels[i+2]-offPixels[i+2]);if(d>4)visible++;
            }
        }
        return new(frame.Pixels,new(id,palette,view.Id,direction,pitch,sample,variant,id+"-"+variant+".png",frame.Shot.PixelSha256,
            frame.Shot.RawHash,selected,frame.Shot.ColourHash!,geometry,unchanged,frame.Shot.PackedParticles,cornerError,signal,visible));
    }

    private static double CheckActualCorners(ShatterParticleArtReview.Audit a)
    {
        var bodies=a.Quads.Where(q=>q.Misc.Z==31).ToArray();
        if(bodies.Length!=a.Selected.Length)throw new InvalidDataException("Cannot join actual projected Shatter bodies to identities.");
        var pose=a.Pose!.Value;var view=new ArView(1280,720,Width,Height,PictureFit.Fit,false);var basis=ArParticleFrame.ForNotes(a.Direction);var error=0.0;
        for(var i=0;i<bodies.Length;i++)
        {
            var p=a.Selected[i];var q=bodies[i];var unit=pose.KeyboardWidthDepthRatio/Math.Max(1,a.Width);
            var anchor=KeyboardLayout.Standard88.CenterX(p.Pitch)*pose.KeyboardWidthDepthRatio;
            var lateral=(p.X/Math.Max(1,a.Width)-PianoStage.KeyCenterOf(p.Pitch))*pose.KeyboardWidthDepthRatio;
            var center=basis.Point(new(anchor,.12,0),lateral,Math.Clamp((a.HitY-p.Y)*unit,-.2,2.4));
            if(!ArParticleBillboard.TryAligned(pose,view,center,basis.Up,out var right,out var up))throw new InvalidDataException("No independent tangent basis.");
            // Deliberately independent of BillboardAxes(): compare actual uploaded corners/weights to the Stage clock.
            var angle=p.Phase+p.Age*7.3f;var c=MathF.Cos(angle);var s=MathF.Sin(angle);
            var axisX=right*c-up*s;var axisY=right*-s-up*c;var size=Math.Clamp(p.Size*unit,.0015,.16);
            if(!Ar3DNoteProjection.TryProjectBillboard(pose,view,center,axisX,axisY,size*1.2,size*.42,out var patch))
                throw new InvalidDataException("Independent Shatter projection failed.");
            var expected=new[]{patch.BackLeft,patch.BackRight,patch.FrontRight,patch.FrontLeft};
            var actual=new[]{(q.A.X,q.A.Y,q.W.X),(q.A.Z,q.A.W,q.W.Y),(q.B.X,q.B.Y,q.W.Z),(q.B.Z,q.B.W,q.W.W)};
            for(var j=0;j<4;j++)error=Math.Max(error,Math.Max(Math.Abs(actual[j].Item1-expected[j].X),Math.Max(Math.Abs(actual[j].Item2-expected[j].Y),Math.Abs(actual[j].Item3-expected[j].W))));
        }
        if(error>0.0003)throw new InvalidDataException("Actual Shatter packet corners disagree with independent Stage-clock projection: "+error);
        return error;
    }
    private static void CheckPair(Record before,Record after,bool ar,bool active)
    {
        if(before.RawHash!=after.RawHash || before.SelectedHash!=after.SelectedHash || before.ColourHash!=after.ColourHash || before.UnchangedHash!=after.UnchangedHash || before.Count!=after.Count)
            throw new InvalidDataException("Billboard candidate changed state, ordered identities, material or non-body geometry: "+after.Id);
        if(!ar && before.PixelSha256!=after.PixelSha256)throw new InvalidDataException("Stage pixels changed in AR-only billboard trial.");
        if(ar && active && before.GeometryHash==after.GeometryHash)throw new InvalidDataException("AR billboard did not change actual geometry: "+after.Id);
        if((!ar || !active) && before.GeometryHash!=after.GeometryHash)throw new InvalidDataException("Unexpected geometry delta: "+after.Id);
    }
    private static byte[] Crop(byte[] pixels,int x,int y,int w,int h)
    {
        var result=new byte[w*h*4];for(var row=0;row<h;row++)Buffer.BlockCopy(pixels,((y+row)*Width+x)*4,result,row*w*4,w*4);return result;
    }
    private static void Write(string path,byte[] pixels,int width,int height)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);var encoder=new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,width*4)));
        using var stream=File.Create(path);encoder.Save(stream);
    }
    private static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
}
