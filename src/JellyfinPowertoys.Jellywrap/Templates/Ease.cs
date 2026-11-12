using System;
using System.Globalization;
using System.Linq;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal static class Ease
{
    private const double CubicPower = 3;
    private const double QuadraticPower = 2;
    private const double DefaultOvershoot = 1.70158;
    private const double DefaultStiffness = 170;
    private const double DefaultDamping = 26;
    private const double BounceGain = 7.5625;
    private const double BounceSpan = 2.75;
    public static Func<double, double> Parse(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return progress => progress;
        }

        var colon = name.IndexOf(':', StringComparison.Ordinal);
        var curve = colon < 0 ? name : name[..colon];
        var arguments = colon < 0
            ? []
            : name[(colon + 1)..].Split(',').Select(argument => double.Parse(argument, CultureInfo.InvariantCulture)).ToArray();
        double Argument(int index, double fallback)
        {
            return arguments.Length > index ? arguments[index] : fallback;
        }

        return curve switch
        {
            "linear" => progress => progress,
            "step" => progress => progress < 1 ? 0 : 1,
            "in" => In(Argument(0, CubicPower)),
            "out" => Out(Argument(0, CubicPower)),
            "in-out" => InOut(Argument(0, CubicPower)),
            "quad-in" => In(QuadraticPower),
            "quad-out" => Out(QuadraticPower),
            "quad-in-out" => InOut(QuadraticPower),
            "sine-in" => progress => 1 - Math.Cos(progress * Math.PI / 2),
            "sine-out" => progress => Math.Sin(progress * Math.PI / 2),
            "sine-in-out" => progress => -(Math.Cos(Math.PI * progress) - 1) / 2,
            "back" => Back(Argument(0, DefaultOvershoot)),
            "bounce" => Bounce,
            "spring" => Spring(Argument(0, DefaultStiffness), Argument(1, DefaultDamping)),
            "steps" => Steps(Argument(0, 1)),
            _ => throw new FormatException($"There is no easing \"{name}\"."),
        };
    }

    private static Func<double, double> In(double power)
    {
        return progress => Math.Pow(progress, power);
    }

    private static Func<double, double> Out(double power)
    {
        return progress => 1 - Math.Pow(1 - progress, power);
    }

    private static Func<double, double> InOut(double power)
    {
        return progress => progress < 0.5 ? Math.Pow(2, power - 1) * Math.Pow(progress, power) : 1 - (Math.Pow((-2 * progress) + 2, power) / 2);
    }

    private static Func<double, double> Back(double overshoot)
    {
        return progress => 1 + ((overshoot + 1) * Math.Pow(progress - 1, 3)) + (overshoot * Math.Pow(progress - 1, 2));
    }

    private static Func<double, double> Steps(double count)
    {
        return progress => progress >= 1 ? 1 : Math.Floor(progress * count) / count;
    }

    private static double Bounce(double progress)
    {
        return progress switch
        {
            < 1 / BounceSpan => BounceGain * progress * progress,
            < 2 / BounceSpan => (BounceGain * (progress -= 1.5 / BounceSpan) * progress) + 0.75,
            < 2.5 / BounceSpan => (BounceGain * (progress -= 2.25 / BounceSpan) * progress) + 0.9375,
            _ => (BounceGain * (progress -= 2.625 / BounceSpan) * progress) + 0.984375,
        };
    }

    private static Func<double, double> Spring(double stiffness, double damping)
    {
        var naturalFrequency = Math.Sqrt(stiffness);
        var dampingRatio = damping / (2 * naturalFrequency);
        var dampedFrequency = naturalFrequency * Math.Sqrt(Math.Max(1e-9, 1 - (dampingRatio * dampingRatio)));
        return progress => progress >= 1
            ? 1
            : 1 - (Math.Exp(-dampingRatio * naturalFrequency * progress)
                * (Math.Cos(dampedFrequency * progress) + (dampingRatio * naturalFrequency / dampedFrequency * Math.Sin(dampedFrequency * progress))));
    }
}
