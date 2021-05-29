using System;
using System.Collections.Generic;
using System.Text;
using FrooxEngine;

namespace MinecraftNeos
{
    public readonly struct MinecraftMaterialGroup : IEquatable<MinecraftMaterialGroup>
    {
        public readonly bool collision;
        public readonly AlphaHandling alphaHandling;

        public MinecraftMaterialGroup(bool collision, AlphaHandling alphaHandling)
        {
            this.collision = collision;
            this.alphaHandling = alphaHandling;
        }

        public bool Equals(MinecraftMaterialGroup other) => this.collision == other.collision &&
            this.alphaHandling == other.alphaHandling;

        public override bool Equals(object obj)
        {
            if (obj is MinecraftMaterialGroup other)
                return Equals(other);

            return false;
        }

        public override int GetHashCode()
        {
            int hashCode = 1847732829;
            hashCode = hashCode * -1521134295 + collision.GetHashCode();
            hashCode = hashCode * -1521134295 + alphaHandling.GetHashCode();
            return hashCode;
        }

        public static bool operator ==(MinecraftMaterialGroup left, MinecraftMaterialGroup right) => left.Equals(right);
        public static bool operator !=(MinecraftMaterialGroup left, MinecraftMaterialGroup right) => !(left == right);

        public override string ToString() => $"{alphaHandling}{(collision ? "_Collision" : "")}";
    }

    public static class MinecraftMaterialClassifier
    {
        public static MinecraftMaterialGroup Classify(string name)
        {
            switch (name)
            {
                case "Cobweb":
                case "Vines":
                case "Sugar_Cane":
                case "Wheat":
                case "Potato":
                case "Carrot":
                case "Lily_Pad":
                case "Ladder":
                case "Monster_Spawner":
                case "Cactus":
                    return new MinecraftMaterialGroup(true, AlphaHandling.AlphaClip);
            }

            var names = NameHeuristicsHelper.SplitName(name);

            if (names.Contains("door") || names.Contains("torch"))
                return new MinecraftMaterialGroup(false, AlphaHandling.AlphaClip);

            if (names.Contains("glass") || names.Contains("rail") || names.Contains("leaves"))
                return new MinecraftMaterialGroup(true, AlphaHandling.AlphaClip);

            if (names.Contains("water"))
                return new MinecraftMaterialGroup(false, AlphaHandling.AlphaBlend);

            if (names.Contains("ice"))
                return new MinecraftMaterialGroup(true, AlphaHandling.AlphaBlend);

            if (names.Contains("redstone") && names.Contains("wire"))
                return new MinecraftMaterialGroup(false, AlphaHandling.AlphaClip);

            if (!names.Contains("block") && !names.Contains("path") && !names.Contains("ore"))
            {
                if (names.Contains("grass") || names.Contains("mushroom") || names.Contains("seagrass") 
                    || names.Contains("tallgrass") || names.Contains("rose") || names.Contains("lily")
                    || names.Contains("fern") || names.Contains("bush") || names.Contains("kelp") || names.Contains("daisy")
                    || names.Contains("lilac") || names.Contains("sunflower") || names.Contains("peony")
                    || names.Contains("dandelion") || names.Contains("poppy") || names.Contains("sapling")
                    || names.Contains("fire"))
                    return new MinecraftMaterialGroup(false, AlphaHandling.AlphaClip);

                if (names.Contains("snow"))
                    return new MinecraftMaterialGroup(false, AlphaHandling.Opaque);
            }

            // default is solid and opaque
            return new MinecraftMaterialGroup(true, AlphaHandling.Opaque);
        }
    }
}
