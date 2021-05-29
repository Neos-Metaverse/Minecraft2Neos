using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using FrooxEngine;
using BaseX;

namespace MinecraftNeos
{
    public class MinecraftNeosData
    {
        const int RENDER_DISTANCE = 512;
        const int SEA_LEVEL = 62;

        public World World { get; private set; }
        public Slot MapRoot { get; private set; }
        public Slot GroupsRoot { get; private set; }
        public Slot MaterialsRoot { get; private set; }
        public Slot TimestampRoot { get; private set; }
        public int GroupSize { get; private set; }

        public TimeSpan Elapsed => DateTime.UtcNow - _importStart;

        Dictionary<int2, long> _chunkTimestamps = new Dictionary<int2, long>();
        Dictionary<int2, long> _newTimestamps = new Dictionary<int2, long>();

        DateTime _importStart;
        int _processedChunks;

        public int TotalChunkCount => _processedChunks;
        public int UpdatedChunkCount => _newTimestamps.Count;

        public MinecraftNeosData(World world, int groupSize)
        {
            this.World = world;

            _importStart = DateTime.UtcNow;

            MapRoot = world.RootSlot.FindOrAdd("Minecraft Map");

            // offset by half so it's compatible with the in-game Minecraft snapping blocks
            // also make sea level be at 0 Y in Neos
            MapRoot.LocalPosition = float3.One * -0.5f + float3.Down * SEA_LEVEL;

            GroupsRoot = MapRoot.FindOrAdd("Chunk Groups");
            MaterialsRoot = MapRoot.FindOrAdd("Materials");
            GroupSize = groupSize;

            // Scan the chunk timestamps
            foreach(var group in GroupsRoot.Children)
            {
                var timestamps = group.Find("Timestamps");

                if (timestamps == null)
                    continue;

                foreach(var timestamp in timestamps.Children)
                {
                    if (!int2.TryParse(timestamp.Name, out int2 chunkCoordinate))
                        continue;

                    var timestampField = timestamp.GetComponent<ValueField<long>>();

                    if (timestampField == null)
                        continue;

                    _chunkTimestamps.Add(chunkCoordinate, timestampField.Value);
                }
            }
        }

        public int2 ChunkCoordinateToGroup(int2 globalChunkCoordinate)
        {
            var coordinate = globalChunkCoordinate;

            coordinate -= (int2.One * GroupSize - 1).Mask(coordinate < 0);
            coordinate = coordinate / GroupSize;

            return coordinate;
        }

        public bool ShouldUpdateChunk(int2 globalChunkCoordinate, long timestamp)
        {
            _processedChunks++;

            if (_chunkTimestamps.TryGetValue(globalChunkCoordinate, out long currentTimestamp) && currentTimestamp == timestamp)
                return false;

            _newTimestamps.Add(globalChunkCoordinate, timestamp);

            // needs an update
            return true;
        }

        public void WriteNewTimestamps()
        {
            foreach(var timestamp in _newTimestamps)
            {
                var groupCoordinate = ChunkCoordinateToGroup(timestamp.Key);
                var groupRoot = GetGroupRoot(groupCoordinate);

                var field = groupRoot.FindOrAdd("Timestamps").FindOrAdd(timestamp.Key.ToString()).GetComponentOrAttach<ValueField<long>>();

                field.Value.Value = timestamp.Value;
            }
        }

        public void FinishImport()
        {
            WriteNewTimestamps();
        }

        public Slot GetGroupRoot(int2 coordinate)
        {
            var slotName = coordinate.ToString();
            var root = GroupsRoot.Find(slotName);

            if (root != null)
                return root;

            root = GroupsRoot.AddSlot(slotName);
            root.LocalPosition = coordinate.x_y * GroupSize * new int3(1, 1, -1);

            return root;
        }

        public async Task SetupGroup(Slot root, GroupData groupData)
        {
            var modelRoot = root.FindChild(s => s.Name.EndsWith(".obj"));

            // get rid of any empty slots
            WorldOptimizer.CleanupEmptySlots(modelRoot);

            var materialGroups = new DictionaryList<MinecraftMaterialGroup, Slot>();

            foreach(var child in modelRoot.Children)
            {
                var group = MinecraftMaterialClassifier.Classify(child.Name);
                UniLog.Log($"Classification {child.Name} - {group}");
                materialGroups.Add(group, child);

                await SetupMaterials(child.GetComponentInChildren<MeshRenderer>(), child.Name, group);
            }

            // wait for the mesh assets to actually load to prevent baking from happening too soon and missing some of them
            while (!modelRoot.ForeachComponentInChildren<MeshRenderer>(m => m.Mesh.IsAssetAvailable))
                await new NextUpdate();

            var geometryRoot = root.AddSlot("Geometry");
            var assetsRoot = root.AddSlot("Assets");

            foreach(var group in materialGroups)
            {
                var groupRoot = geometryRoot.AddSlot(group.Key.ToString());

                foreach (var mesh in group.Value)
                    mesh.Parent = groupRoot;

                var bakeResult = await MeshBaker.BakeMeshes(groupRoot, true, false, grabbable: ComponentHandling.NeverAdd,
                    collider: group.Key.collision ? ComponentHandling.AlwaysAdd : ComponentHandling.NeverAdd, assetsSlot: assetsRoot);

                if (group.Key.collision)
                    bakeResult.GetComponentInChildren<MeshCollider>().SetCharacterCollider();
            }

            // setup lights
            if (groupData.lightSources.Count > 0)
            {
                var lightsRoot = geometryRoot.AddSlot("Lights");

                foreach (var lightPos in groupData.lightSources)
                {
                    var lightRoot = lightsRoot.AddSlot("Light");
                    var light = lightRoot.AttachComponent<Light>();

                    light.Range.Value = 6;
                    light.Intensity.Value = 1f;
                    light.Color.Value = new color(1f, 0.9f, 0.5f);

                    var groupLightPos = new int3(GroupSize - lightPos.x - 1,  lightPos.yz);

                    lightRoot.LocalPosition = groupLightPos - new float3(GroupSize * 0.5f, 0, GroupSize * 0.5f) + float3.One * 0.5f;
                }

                // disable them by default since they can be heavy
                lightsRoot.ActiveSelf = false;

                var lightsVariableDriver = geometryRoot.AttachComponent<DynamicValueVariableDriver<bool>>();

                lightsVariableDriver.VariableName.Value = "World/Minecraft.Lights";
                lightsVariableDriver.DefaultValue.Value = false;
                lightsVariableDriver.Target.Target = lightsRoot.ActiveSelf_Field;
            }

            modelRoot.Destroy();

            // Setup culling
            var cullingRoot = root.AddSlot("Culling");

            var trigger = cullingRoot.AttachComponent<SphereCollider>();

            trigger.Type.Value = ColliderType.Trigger;
            trigger.Radius.Value = RENDER_DISTANCE;
            trigger.Offset.Value = float3.Up * 128;

            var userTracker = cullingRoot.AttachComponent<ColliderUserTracker>();
            geometryRoot.ActiveSelf_Field.DriveFrom(userTracker.IsLocalUserInside);

            foreach(var renderer in geometryRoot.GetComponentsInChildren<MeshRenderer>())
            {
                var refDriver = renderer.Slot.AttachComponent<BooleanReferenceDriver<IAssetProvider<Mesh>>>();

                refDriver.TrueTarget.Target = renderer.Mesh.Target;
                refDriver.TargetReference.Target = renderer.Mesh;
                refDriver.State.DriveFrom(userTracker.IsLocalUserInside);
            }

            foreach(var meshCollider in geometryRoot.GetComponentsInChildren<MeshCollider>())
            {
                var refDriver = meshCollider.Slot.AttachComponent<BooleanReferenceDriver<IAssetProvider<Mesh>>>();

                refDriver.TrueTarget.Target = meshCollider.Mesh.Target;
                refDriver.TargetReference.Target = meshCollider.Mesh;
                refDriver.State.DriveFrom(userTracker.IsLocalUserInside);
            }
        }

        public async Task SetupMaterials(MeshRenderer renderer, string groupName, MinecraftMaterialGroup group)
        {
            for(int i = 0; i < renderer.Materials.Count; i++)
            {
                var mat = (MaterialProvider)renderer.Materials[i];

                var name = mat.Slot.Name;
                var names = NameHeuristicsHelper.SplitName(name);

                var sharedMaterial = MaterialsRoot.Find(name)?.GetComponent<MaterialProvider>();

                if(sharedMaterial == null)
                {
                    var root = MaterialsRoot.AddSlot(name);

                    sharedMaterial = root.DuplicateComponent(mat);

                    if(names.Contains("water"))
                        sharedMaterial = SetupWater(names, sharedMaterial);

                    if (names.Contains("grass") && !names.Contains("block") && !names.Contains("double") && !names.Contains("tall")
                        && !names.Contains("path"))
                        sharedMaterial = SetupGrass(sharedMaterial);

                    if(sharedMaterial is IPBS_Material pbs)
                    {
                        if(pbs is IPBS_Metallic metallic)
                        {
                            metallic.Metallic = 0;
                            metallic.Smoothness = 0;
                        }

                        if(pbs is IPBS_Specular specular)
                            specular.SpecularColor = color.Clear;

                        switch(group.alphaHandling)
                        {
                            case AlphaHandling.AlphaBlend:
                                pbs.BlendMode = BlendMode.Alpha;
                                break;

                            case AlphaHandling.AlphaClip:
                                pbs.BlendMode = BlendMode.Cutout;
                                break;
                        }
                    }
                }

                renderer.Materials[i] = sharedMaterial;
            }
        }

        public MaterialProvider SetupGrass(MaterialProvider material)
        {
            if(material is PBS_Specular specular)
            {
                var displace = material.Slot.AttachComponent<PBS_DisplaceSpecular>();
                MaterialHelper.CopyMaterialProperties(specular, displace);

                var gradient = material.Slot.AttachComponent<GradientStripTexture>();

                gradient.SetupGradientFromTo(color.White, color.Black);
                gradient.Orientation = GradientStripTexture.StripOrientation.Vertical;

                displace.VertexDisplaceMap.Target = gradient;

                var time = material.Slot.AttachComponent<FrooxEngine.LogiX.Input.TimeNode>();
                var sine = material.Slot.AttachComponent<FrooxEngine.LogiX.Math.Sin_Float>();
                var mul = material.Slot.AttachComponent<FrooxEngine.LogiX.Operators.Mul_Float>();
                var magnitude = material.Slot.AttachComponent<FrooxEngine.LogiX.Input.FloatInput>();

                magnitude.CurrentValue = 0.1f;

                sine.N.Target = time;
                mul.A.Target = sine;
                mul.B.Target = magnitude;

                var driver = material.Slot.AttachComponent<FrooxEngine.LogiX.DriverNode<float>>();

                driver.Source.Target = mul;
                driver.DriveTarget.Target = displace.VertexDisplaceMagnitude;

                specular.Destroy();

                return displace;
            }

            return material;
        }

        public MaterialProvider SetupWater(List<string> names, MaterialProvider material)
        {
            if(material is PBS_Specular specular)
            {
                var dualSided = specular.Slot.AttachComponent<PBS_DualSidedSpecular>();
                MaterialHelper.CopyMaterialProperties(specular, dualSided);

                // Avoid Z-fighting on edges
                dualSided.OffsetFactor.Value = 1;
                dualSided.OffsetUnits.Value = 1;

                if(names.Contains("flow"))
                {
                    var panner = material.Slot.AttachComponent<Panner2D>();
                    panner.Target = dualSided.TextureOffset;
                    panner.Speed = new float2(0f, 1f);
                }

                specular.Destroy();

                return dualSided;
            }

            return material;
        }
    }
}
