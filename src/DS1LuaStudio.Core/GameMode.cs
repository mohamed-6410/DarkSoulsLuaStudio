namespace DS1LuaStudio.Core;

public enum GameMode { DS1, DS3 }

public static class GameModeExtensions
{
    public static string DisplayName(this GameMode mode) => mode == GameMode.DS3 ? "Dark Souls III" : "Dark Souls PTDE";
    public static int SizeTSize(this GameMode mode) => mode == GameMode.DS3 ? 8 : 4;
}
