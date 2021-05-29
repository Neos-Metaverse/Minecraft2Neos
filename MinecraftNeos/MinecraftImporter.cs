using System;
using System.IO;
using System.Text;
using System.Linq;
using Substrate;
using BaseX;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using FrooxEngine;

namespace MinecraftNeos
{
    public static class MinecraftImporter
    {
        public static string MinewaysPath = @"C:\Program Files\mineways\mineways.exe";
        public static string WorkingDirectory = null;

        [FolderImporter("Importer.Folder.Minecraft", "Importer.Folder.Minecraft.Description")]
        public static void ImportMinecraftFolder(Slot root, string path)
        {
            root.World.Coroutines.StartTask(async () =>
            {
                root.PersistentSelf = false;

                var indicator = root.AttachComponent<NeosLogoMenuProgress>();
                indicator.Spawn(root.GlobalPosition, 0.05f, true);
                indicator.UpdateProgress(-1f, "Waiting", "");

                try
                {
                    await SyncMinecraftWorld(root.World, path, progress: indicator);
                }
                catch(Exception ex)
                {
                    UniLog.Error("Exception importing Minecraft world:\n" + ex);
                    indicator.ProgressFail("Failed!");
                }
            });
        }

        public static async Task SyncMinecraftWorld(World world, string minecraftWorldPath, int groupSize = 64, IProgressIndicator progress = null)
        {
            progress?.UpdateProgress(-1, "Searching for Mineways...", "");

            // figure out actual mineways path
            string minewaysExecutable = MinewaysPath;

            if (!File.Exists(minewaysExecutable))
                minewaysExecutable = Path.Combine(world.Engine.AppPath, @"Tools\Mineways\mineways.exe");

            if (!File.Exists(minewaysExecutable))
                throw new Exception("Mineways.exe not found!");

            var data = new MinecraftNeosData(world, groupSize);

            await new ToBackground();

            var workingDir = WorkingDirectory;

            if (string.IsNullOrWhiteSpace(workingDir))
                workingDir = Path.Combine(world.Engine.CachePath, "MinecraftStaging");

            workingDir = Path.Combine(workingDir, Guid.NewGuid().ToString());

            // cleanup working directory first
            if (Directory.Exists(workingDir))
                Directory.Delete(workingDir, true);

            Directory.CreateDirectory(workingDir);

            var groups = await ExportGroups(data, minewaysExecutable, minecraftWorldPath, workingDir, groupSize, progress);

            await new ToWorld();

            progress?.UpdateProgress(-1, "Importing chunks", "");

            var importSettings = ModelImportSettings.PBS(false, false, false, false, false, true);

            importSettings.Center = false;
            importSettings.CalculateTextureAlpha = false;
            importSettings.ForceNoMipmaps = true;
            importSettings.ForcePointFiltering = true;
            importSettings.ForceCompression = CodeX.TextureCompression.RawRGBA;

            int count = 0;

            foreach(var group in groups.OrderBy(g => g.Key.Magnitude))
            {
                progress?.UpdateProgress(count / (float)groups.Count, $"Generating group {group.Key}", "Importing geometry");

                var groupRoot = data.GetGroupRoot(group.Key);

                // cleanup the chunk first
                groupRoot.DestroyChildren();

                var originalAssets = groupRoot.AddSlot("Import Assets");

                // import the chunk model
                await ModelImporter.ImportModelAsync(group.Value.file, groupRoot, importSettings, originalAssets);

                progress?.UpdateProgress(count / (float)groups.Count, $"Generating group {group.Key}", "Setting up group");

                await data.SetupGroup(groupRoot, group.Value);

                originalAssets.DestroyPreservingAssets();

                count++;
            }

            data.FinishImport();

            UniLog.Log($"Minecraft Map Import Finished! Processed chunks: {data.TotalChunkCount}, New/Updated chunks: {data.UpdatedChunkCount}, " +
                $"Elapsed: {data.Elapsed}");

            progress?.ProgressDone("Map imported!");
        }

        static async Task<Dictionary<int2, GroupData>> ExportGroups(MinecraftNeosData data, string minewaysExe, string minecraftWorldPath, string workingDir, int groupSize, IProgressIndicator progress)
        {
            var groupsPath = Path.Combine(workingDir, "Groups");
            var scriptPath = Path.Combine(workingDir, "Script.mwscript");

            Directory.CreateDirectory(groupsPath);

            var script = new StringBuilder();

            script.AppendLine($"Minecraft world: {minecraftWorldPath}");
            script.AppendLine("Set render type: Wavefront OBJ absolute indices");
            script.AppendLine("File type: Export tiles for textures to directory texture");
            script.AppendLine("Create block faces at the borders: no");
            script.AppendLine("Center model: YES");

            progress?.UpdateProgress(-1, "Analyzing Minecraft Data", "");

            var minecraftWorld = NbtWorld.Open(minecraftWorldPath);
            var chunkManager = minecraftWorld.GetChunkManager();

            var groups = new Dictionary<int2, GroupData>();

            int count = 0;

            foreach (var chunk in chunkManager)
            {
                if(chunk.UsesPalette)
                {
                    // TODO!!! New format that uses palette for blocks isn't supported yet, so the actual block data won't be extracted
                    if (chunk.Status != "full")
                        continue;
                }
                else
                {
                    if (chunk.Blocks == null)
                        continue;

                    if (chunk.Status != null && chunk.Status != "full")
                        continue;

                    if (chunk.Blocks.IsEmpty())
                        continue;
                }

                progress?.UpdateProgress(-1, "Analyzing Minecraft Data", $"Chunk {chunk.X}x{chunk.Z}");

                count++;

                /*var chunkXsize = chunk.Blocks.XDim;
                var chunkYsize = chunk.Blocks.YDim;
                var chunkZsize = chunk.Blocks.ZDim;*/

                // TODO!!! Currently the chunks that use palette don't have blocks so can't fetch it from them
                // Need to update Substrate library. Currently the size is fixed so this is ok for now
                var chunkXsize = 16;
                var chunkYsize = 256;
                var chunkZsize = 16;

                var chunkStartX = chunk.X * chunkXsize;
                var chunkStartZ = chunk.Z * chunkZsize;

                var chunkCoordinate = new int2(chunkStartX, chunkStartZ);

                if (!data.ShouldUpdateChunk(chunkCoordinate, chunk.LastUpdate))
                    continue;

                var coordinate = data.ChunkCoordinateToGroup(chunkCoordinate);

                var groupStartX = coordinate.x * groupSize;
                var groupStartZ = coordinate.y * groupSize;

                var groupEndX = groupStartX + groupSize;
                var groupEndZ = groupStartZ + groupSize;

                var offsetInGroupX = chunkStartX - groupStartX;
                var offsetInGroupZ = chunkStartZ - groupStartZ;

                if (!groups.TryGetValue(coordinate, out GroupData groupData))
                {
                    var filePath = $@"{groupsPath}\Group_{coordinate.x}x{coordinate.y}.obj";

                    groupData = new GroupData(filePath);
                    groups.Add(coordinate, groupData);

                    script.AppendLine($"Selection location min to max: {groupStartX}, 0, {groupStartZ} to " +
                        $"{groupEndX-1}, {chunkYsize}, {groupEndZ-1}");

                    script.AppendLine($@"Export for Rendering: {filePath}");
                }

                if (!chunk.UsesPalette)
                {
                    for (int x = 0; x < chunkXsize; x++)
                        for (int y = 0; y < chunkYsize; y++)
                            for (int z = 0; z < chunkZsize; z++)
                            {
                                var block = chunk.Blocks.GetBlockRef(x, y, z);

                                if (block.Info == BlockInfo.Torch ||
                                    block.Info == BlockInfo.Fire)
                                {
                                    groupData.lightSources.Add(new int3(x + offsetInGroupX, y, z + offsetInGroupZ));
                                }
                            }
                }
            }

            script.AppendLine("Close");

            progress?.UpdateProgress(-1, "Writing export script", "");

            File.WriteAllText(scriptPath, script.ToString());

            progress?.UpdateProgress(-1, "Exporting chunk geometry", "");

            var info = new ProcessStartInfo(minewaysExe, $"-s none \"{scriptPath}\"");
            info.UseShellExecute = true;

            await Task.Run(() =>
            {
                var process = Process.Start(info);
                process.WaitForExit();

            }).ConfigureAwait(false);

            return groups;
        }
    }
}
