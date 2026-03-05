namespace DohShield.Engine;

/// <summary>
/// WinDivert 攔截引擎狀態
/// 移植自 Android VpnState.kt
/// </summary>
public enum EngineState
{
    Stopped,
    Starting,
    Running,
    Error
}
