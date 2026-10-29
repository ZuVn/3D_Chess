using UnityEngine;

namespace ChessGame
{
    public enum Surface { Plain, Wood, Marble }

    public sealed class BoardTheme
    {
        public string Name;
        public Color Light, Dark, Frame, Coordinates;
        public Surface Squares, FrameSurface;
        public float Smoothness;
    }

    public sealed class PieceTheme
    {
        public string Name;
        public Color White, Black;
        public Surface Surface;
        public float Metallic, Smoothness;
    }

    public sealed class EnvironmentTheme
    {
        public string Name;
        public Color Background, Table, Sky, Equator, Ground, Sun;
        public float SunIntensity;
        public Surface TableSurface;
    }

    public static class Themes
    {
        static Color C(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }

        public static readonly BoardTheme[] Boards =
        {
            new BoardTheme { Name = "Classic Wood", Light = C("#E3C79B"), Dark = C("#8A5A35"), Frame = C("#4A2C1A"),
                Coordinates = C("#E9D3AE"), Squares = Surface.Wood, FrameSurface = Surface.Wood, Smoothness = 0.55f },
            new BoardTheme { Name = "Dark Modern", Light = C("#6E7681"), Dark = C("#2B3038"), Frame = C("#16191E"),
                Coordinates = C("#9AA4B2"), Squares = Surface.Plain, FrameSurface = Surface.Plain, Smoothness = 0.8f },
            new BoardTheme { Name = "Light Minimalist", Light = C("#F4F1EA"), Dark = C("#B9C2C9"), Frame = C("#E2DED6"),
                Coordinates = C("#7C858D"), Squares = Surface.Plain, FrameSurface = Surface.Plain, Smoothness = 0.25f },
            new BoardTheme { Name = "Marble", Light = C("#ECEAE4"), Dark = C("#4E6B62"), Frame = C("#2F3B37"),
                Coordinates = C("#D7DED9"), Squares = Surface.Marble, FrameSurface = Surface.Marble, Smoothness = 0.85f },
        };

        public static readonly PieceTheme[] Pieces =
        {
            new PieceTheme { Name = "Ivory & Ebony", White = C("#F1E9D8"), Black = C("#262321"), Surface = Surface.Plain, Metallic = 0f, Smoothness = 0.62f },
            new PieceTheme { Name = "Boxwood & Rosewood", White = C("#E6C48E"), Black = C("#5A2E1E"), Surface = Surface.Wood, Metallic = 0f, Smoothness = 0.5f },
            new PieceTheme { Name = "Modern Matte", White = C("#FAFAFA"), Black = C("#3A3F47"), Surface = Surface.Plain, Metallic = 0f, Smoothness = 0.3f },
            new PieceTheme { Name = "Gold & Silver", White = C("#E8C766"), Black = C("#9DA3AB"), Surface = Surface.Plain, Metallic = 0.9f, Smoothness = 0.75f },
        };

        public static readonly EnvironmentTheme[] Environments =
        {
            new EnvironmentTheme { Name = "Study", Background = C("#1B1714"), Table = C("#3B2A20"), Sky = C("#8C8478"), Equator = C("#4D453D"),
                Ground = C("#211C18"), Sun = C("#FFF1DC"), SunIntensity = 1.6f, TableSurface = Surface.Wood },
            new EnvironmentTheme { Name = "Night", Background = C("#0E1320"), Table = C("#1A2133"), Sky = C("#6E7EA3"), Equator = C("#353D52"),
                Ground = C("#10131B"), Sun = C("#DCE6FF"), SunIntensity = 1.4f, TableSurface = Surface.Plain },
            new EnvironmentTheme { Name = "Daylight", Background = C("#B9C6CF"), Table = C("#8F9AA1"), Sky = C("#D9E4EC"), Equator = C("#A6AEB3"),
                Ground = C("#5E6468"), Sun = C("#FFFFFF"), SunIntensity = 1.5f, TableSurface = Surface.Plain },
        };

        // Interaction colours (shared by every theme; each also differs in shape, not only colour).
        public static readonly Color Selected = new Color(1f, 0.85f, 0.25f, 0.55f);
        public static readonly Color LegalMove = new Color(0.08f, 0.08f, 0.08f, 0.42f);
        public static readonly Color Capture = new Color(0.85f, 0.2f, 0.15f, 0.75f);
        public static readonly Color LastMove = new Color(1f, 0.9f, 0.35f, 0.32f);
        public static readonly Color Check = new Color(1f, 0.12f, 0.08f, 0.7f);
        public static readonly Color Hover = new Color(1f, 1f, 1f, 0.22f);
        public static readonly Color Illegal = new Color(1f, 0.1f, 0.1f, 0.5f);
    }
}
