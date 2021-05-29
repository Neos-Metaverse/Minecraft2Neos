using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
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
        static HashSet<string> _noCollisionClip = new HashSet<string>()
        {
            "grass", "seagrass", "tallgrass",
            "mushroom", "fern", "bush", "kelp",
            "rose", "lily", "tulip", "daisy", "lilac", "sunflower", "flower", "peony", "dandelion", "poppy", "orchid", "bluet", "cornflower",
            "sapling", "potato", "carrot", "wheat", "beet", "cane", "vines", "beetroot",
            "cobweb", "fire", "vines", 
        };

        public static MinecraftMaterialGroup Classify(string name)
        {
            switch (name)
            {
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
                if (names.Any(n => _noCollisionClip.Contains(n)))
                    return new MinecraftMaterialGroup(false, AlphaHandling.AlphaClip);

                if (names.Contains("snow"))
                    return new MinecraftMaterialGroup(false, AlphaHandling.Opaque);
            }

            // default is solid and opaque
            return new MinecraftMaterialGroup(true, AlphaHandling.Opaque);
        }
    }
}
