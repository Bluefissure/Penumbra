using System.Collections.Generic;
using System.ComponentModel;

namespace Penumbra.Game
{
    public enum CustomizationType : byte
    {
        Unknown,
        Body,
        Tail,
        Face,
        Iris,
        Accessory,
        Hair,
        DecalFace,
        DecalEquip,
        Skin,
        Etc
    }

    public static class CustomizationTypeEnumExtension
    {
        public static string ToSuffix( this CustomizationType value )
        {
            return value switch
            {
                CustomizationType.Face      => "fac",
                CustomizationType.Iris      => "iri",
                CustomizationType.Accessory => "acc",
                CustomizationType.Hair      => "hir",
                CustomizationType.Tail      => "til",
                CustomizationType.Etc       => "etc",
                _                           => throw new InvalidEnumArgumentException()
            };
        }
    }

    public static partial class GameData
    {
        public static readonly Dictionary< string, CustomizationType > SuffixToCustomizationType = new()
        {
            { CustomizationType.Face.ToSuffix(), CustomizationType.Face },
            { CustomizationType.Iris.ToSuffix(), CustomizationType.Iris },
            { CustomizationType.Accessory.ToSuffix(), CustomizationType.Accessory },
            { CustomizationType.Hair.ToSuffix(), CustomizationType.Hair },
            { CustomizationType.Tail.ToSuffix(), CustomizationType.Tail },
            { CustomizationType.Etc.ToSuffix(), CustomizationType.Etc }
        };
    }
}