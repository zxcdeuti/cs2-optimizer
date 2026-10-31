namespace CS2Optimizer.Services;

public record CrosshairState(int Length, int Thickness, int Gap, bool Dot, bool T, bool Outline, int R, int G, int B, int A)
{
    /// <summary>Console line in the post-September-2026 cvar names.</summary>
    public string ToCommand() => string.Join("; ",
        $"cl_crosshair_length {Length}", $"cl_crosshair_thickness {Thickness}", $"cl_crosshair_gap {Gap}",
        $"cl_crosshairdot {(Dot ? 1 : 0)}", $"cl_crosshair_t {(T ? 1 : 0)}",
        $"cl_crosshaircolor_r {R}", $"cl_crosshaircolor_g {G}", $"cl_crosshaircolor_b {B}", $"cl_crosshaircolor_a {A}");
}
