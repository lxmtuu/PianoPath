using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PianoPath;

/// <summary>Selection-policy trial. Same particles/quads; intentional different selected identities under cap pressure.</summary>
internal static class ArParticleBudgetReview
{
    private static readonly JsonSerializerOptions Json=new(){IncludeFields=true,WriteIndented=true};
    internal sealed record Shot(string Id,string Recipe,string Palette,string View,string Phase,int Frame,string Mode,string File,
        string PixelSha256,string RawHash,string PacketHash,string KeyHash,string NonParticleHash,string EligibleHash,string SelectedHash,
        int RawParticles,int Packets,int[] EligibleKeys,int[] Selected,string? InventoryFile,string? InventoryHash,double DrawMilliseconds);
    private sealed record FrameResult(byte[] Pixels,Shot Shot);
    internal static int Render(string output)
    {
        Directory.CreateDirectory(output);var previous=KeyboardLayout.Active;KeyboardLayout.Use(KeyboardLayout.Standard88);
        try
        {
            var root=Path.GetFullPath(Path.Combine(PrismFlowVisualReview.FindSamples(),"..",".."));
            using var baselineStream=new GZipStream(File.OpenRead(Path.Combine(root,"docs/evidence/key-particle-ensemble-v1/manifest.json.gz")),CompressionMode.Decompress);
            using var baseline=JsonDocument.Parse(baselineStream);
            if(baseline.RootElement.GetProperty("sourceRevision").GetString()!="79930e52cb3773f02d9e8e59be2c111236917227")throw new InvalidDataException("Ensemble baseline was silently refreshed.");
            var prior=baseline.RootElement.GetProperty("captures").EnumerateArray().Where(c=>c.GetProperty("Variant").GetString()=="mixed" && c.GetProperty("Light").GetInt32()==100)
                .ToDictionary(c=>c.GetProperty("Id").GetString()!,c=>c.GetProperty("PixelSha256").GetString()!);
            var camera=PrismFlowVisualReview.ReadSyntheticCamera(Path.Combine(PrismFlowVisualReview.FindSamples(),"ar-keyboard-topdown.png"));
            using var renderer=GpuStageRenderer.Create(forceWarp:true);if(!renderer.IsWarp)throw new InvalidOperationException("Budget review requires WARP.");
            var shots=new List<Shot>();
            foreach(var recipe in KeyParticleEnsemblePlan.Recipes)
            foreach(var palette in KeyParticleEnsemblePlan.Palettes)
            foreach(var view in HeldParticleControlPlan.Views)
            foreach(var checkpoint in KeyParticleEnsemblePlan.Checkpoints)
            {
                var frames=new List<FrameResult>();
                foreach(var mode in new[]{"fast","legacy","fair"})
                {
                    var f=Draw(renderer,output,recipe,palette,view,camera,checkpoint,mode);
                    frames.Add(f);shots.Add(f.Shot);
                }
                Check(frames[0],frames[1],frames[2],view.OverCamera);
                if(view.Id is "live-stage" or "ar-reference")
                {
                    var key=$"{recipe.Id}-{palette}-l100-{view.Id}-{checkpoint.Id}-mixed";
                    if(frames[0].Shot.PixelSha256!=prior[key])throw new InvalidDataException("Default-off changed frozen ensemble pixels: "+key);
                }
            }
            // Exact 16-frame/30Hz temporal slice around release. No interpolation or dropped frames.
            foreach(var recipe in KeyParticleEnsemblePlan.Recipes.Take(2))
            for(var f=29;f<=44;f++)
            {
                var a=Draw(renderer,output,recipe,"prism",HeldParticleControlPlan.Views[1],camera,new("motion",f),"legacy");
                var b=Draw(renderer,output,recipe,"prism",HeldParticleControlPlan.Views[1],camera,new("motion",f),"fair");
                Check(null,a,b,true);shots.Add(a.Shot);shots.Add(b.Shot);
            }
            if(shots.Count!=904)throw new InvalidDataException("Incomplete budget matrix.");
            renderer.ObserveArBudgetForReview=null;
            string[] sources=["Gpu/ArParticleBudget.cs","Gpu/GpuLook.cs","Gpu/GpuArLayer.cs","Gpu/GpuArLayer.Effects.cs","Gpu/GpuStageRenderer.cs",
                "Gpu/GpuStageSimulation.cs","Gpu/GpuStageSimulation.Effects.cs","Gpu/GpuStageFeed.cs","Gpu/GpuRenderLoop.cs","Gpu/StageShaders.hlsl",
                "Gpu/HeldParticleAppearance.cs","Gpu/ReleaseParticleAppearance.cs","Stage/CommunityPresets.cs","Stage/PianoVisualSettings.cs","Stage/PianoStage.cs",
                "Stage/PianoStage.Gpu.cs","Camera/ArParticleBillboard.cs","Camera/ArCameraPose.cs","Camera/Ar3DNoteProjection.cs",
                "Diagnostics/KeyParticleFixtures.cs","Diagnostics/HeldParticleControlPlan.cs","Diagnostics/KeyParticleEnsemblePlan.cs",
                "Diagnostics/KeyParticleEnsembleReview.cs","Diagnostics/ArParticleBudgetReview.cs","docs/samples/ar-keyboard-topdown.png"];
            File.WriteAllText(Path.Combine(output,"manifest.json"),JsonSerializer.Serialize(new{schema=1,family="ar-particle-budget",reviewOnly=true,defaultEnabled=false,cap=192,
                sourceRevision=Environment.GetEnvironmentVariable("GITHUB_SHA")??"workspace",adapter=renderer.AdapterName,width=640,height=360,fps=30,seed=KeyParticleEnsemblePlan.Seed,
                baselineRevision="79930e52cb3773f02d9e8e59be2c111236917227",recipes=KeyParticleEnsemblePlan.Recipes,palettes=KeyParticleEnsemblePlan.Palettes,
                checkpoints=KeyParticleEnsemblePlan.Checkpoints,notes=KeyParticleEnsemblePlan.Notes,pedalOnFrame=24,pedalOffFrame=54,
                policy="round-robin origins 0/1/2 then pitches, borrow unused shares; retain legacy draw order; exact under-cap identity",
                eligibility="same production alpha/projectability; each eligible particle projected once into unchanged quad; only selected indices differ",
                lighting="same ensemble mixed/100; held v3/release material at existing depth; no other art/depth flags",
                limitations="not realtime/hardware proof; eligibility scan/queues add review-only CPU work; temporal slice f29..44 is not full lifecycle playback",
                sourceHashes=sources.ToDictionary(p=>p,p=>Hash(p.EndsWith(".png",StringComparison.Ordinal)?File.ReadAllBytes(Path.Combine(root,p)):
                    Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(root,p)).Replace("\r\n","\n")))),captures=shots},Json));
            return shots.Count;
        }
        finally{KeyboardLayout.Use(previous);}
    }
    private static FrameResult Draw(GpuStageRenderer renderer,string output,KeyParticleEnsemblePlan.Recipe recipe,string palette,
        HeldParticleControlPlan.View view,byte[] sourceCamera,KeyParticleEnsemblePlan.Checkpoint checkpoint,string mode)
    {
        GpuArLayer.BudgetTrace? trace=null;var actual=new List<GpuArQuad>();
        renderer.ObserveArBudgetForReview=mode=="fast"?null:t=>trace=t;
        var camera=view.OverCamera?HeldParticleControlPlan.CameraFor(view,sourceCamera):sourceCamera;
        var timer=Stopwatch.StartNew();
        var original=KeyParticleEnsembleReview.Draw(renderer,recipe,palette,100,view,camera,checkpoint,"mixed",mode=="fair",q=>actual.Add(q));timer.Stop();
        var s=original.Shot;var id=$"{recipe.Id}-{palette}-{view.Id}-{checkpoint.Id}-f{checkpoint.Frame}-{mode}";
        string? inventoryFile=null,inventoryHash=null;var eligibleHash=Hash([]);var selectedHash=Hash([]);var nonParticleHash=Hash([]);int[] keys=[],selected=[];
        if(view.OverCamera && mode!="fast")
        {
            if(trace is null || trace.Fair!=(mode=="fair"))throw new InvalidDataException("Missing actual budget trace.");
            keys=trace.Eligible.Select(e=>e.Particle.Pitch+128*e.Particle.Origin).ToArray();selected=trace.Selected;
            var expected=mode=="fair"?ArParticleBudget.Select(trace.Eligible.Select(e=>new ArParticleBudget.Candidate(e.Particle.Pitch,e.Particle.Origin)).ToArray(),192)
                :Enumerable.Range(0,Math.Min(192,keys.Length)).ToArray();
            if(!expected.SequenceEqual(selected))throw new InvalidDataException("Selector did not use the declared policy.");
            if(selected.Length!=Math.Min(192,keys.Length) || selected.Distinct().Count()!=selected.Length || !selected.SequenceEqual(selected.Order()))throw new InvalidDataException("Cap/subset/order violation.");
            var wanted=selected.Select(i=>trace.Eligible[i].Quad).ToArray();
            var emitted=actual.Skip(trace.Start).Take(selected.Length).ToArray();
            if(Hash(JsonSerializer.SerializeToUtf8Bytes(wanted,Json))!=Hash(JsonSerializer.SerializeToUtf8Bytes(emitted,Json)) || renderer.ArParticleFxCount!=selected.Length)
                throw new InvalidDataException("Actual uploaded particle payload differs from selected eligible quads.");
            eligibleHash=Hash(JsonSerializer.SerializeToUtf8Bytes(trace.Eligible.Select(e=>new{e.Particle,e.Particle.MaterialRoute,e.Quad}),Json));
            selectedHash=Hash(JsonSerializer.SerializeToUtf8Bytes(selected.Select(i=>new{Index=i,trace.Eligible[i].Particle,trace.Eligible[i].Particle.MaterialRoute,trace.Eligible[i].Quad}),Json));
            nonParticleHash=Hash(JsonSerializer.SerializeToUtf8Bytes(actual.Take(trace.Start).Concat(actual.Skip(trace.Start+selected.Length)).ToArray(),Json));
            inventoryFile=id+".json.gz";
            using(var stream=File.Create(Path.Combine(output,inventoryFile)))using(var gzip=new GZipStream(stream,CompressionLevel.SmallestSize))
                JsonSerializer.Serialize(gzip,new{trace.Start,trace.Fair,Eligible=trace.Eligible.Select(e=>new{e.Particle,e.Particle.MaterialRoute,e.Quad}),Selected=selected,Actual=emitted},Json);
            inventoryHash=Hash(File.ReadAllBytes(Path.Combine(output,inventoryFile)));
        }
        WritePng(Path.Combine(output,id+".png"),original.Pixels);
        return new(original.Pixels,new(id,recipe.Id,palette,view.Id,checkpoint.Id,checkpoint.Frame,mode,id+".png",s.PixelSha256,s.RawHash,s.PacketHash,s.KeyHash,
            nonParticleHash,eligibleHash,selectedHash,s.RawParticles,s.Packets,keys,selected,inventoryFile,inventoryHash,timer.Elapsed.TotalMilliseconds));
    }
    private static void Check(FrameResult? fast,FrameResult legacy,FrameResult fair,bool ar)
    {
        var a=legacy.Shot;var b=fair.Shot;
        if(fast is not null && (fast.Shot.PixelSha256!=a.PixelSha256 || fast.Shot.PacketHash!=a.PacketHash || fast.Shot.RawHash!=a.RawHash))throw new InvalidDataException("Instrumented legacy is not the original fast-path output.");
        if(a.RawHash!=b.RawHash || a.RawParticles!=b.RawParticles || a.KeyHash!=b.KeyHash || a.Packets!=b.Packets || a.NonParticleHash!=b.NonParticleHash || a.EligibleHash!=b.EligibleHash || !a.EligibleKeys.SequenceEqual(b.EligibleKeys))
            throw new InvalidDataException("Selection trial altered state, eligibility, non-particle packets or count.");
        if((!ar || a.EligibleKeys.Length<=192) && (a.PacketHash!=b.PacketHash || a.PixelSha256!=b.PixelSha256))throw new InvalidDataException("Under-cap or Stage output changed.");
        if(ar && a.EligibleKeys.Length>192)
        {
            var present=a.EligibleKeys.Select(k=>k/128).Distinct().ToArray();
            foreach(var role in present)
            {
                var pitches=a.EligibleKeys.Where(k=>k/128==role).Select(k=>k%128).Distinct().ToArray();
                if(pitches.Length*present.Length<=192 && pitches.Any(p=>!b.Selected.Any(i=>b.EligibleKeys[i]==p+role*128)))throw new InvalidDataException("Eligible role/pitch is still starved.");
            }
        }
    }
    private static void WritePng(string path,byte[] pixels)
    {
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(BitmapSource.Create(640,360,96,96,PixelFormats.Bgra32,null,pixels,640*4)));
        using(var stream=File.Create(path))png.Save(stream);
        using var input=File.OpenRead(path);var image=BitmapDecoder.Create(input,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
        var bitmap=new FormatConvertedBitmap(image.Frames[0],PixelFormats.Bgra32,null,0);var actual=new byte[pixels.Length];bitmap.CopyPixels(actual,640*4,0);
        if(!actual.SequenceEqual(pixels))throw new InvalidDataException("PNG changed native pixels.");
    }
    private static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
}
