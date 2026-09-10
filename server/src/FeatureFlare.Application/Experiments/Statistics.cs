namespace FeatureFlare.Application.Experiments;

/// <summary>Result of comparing a treatment conversion rate against the control.</summary>
public sealed record ProportionComparison(
    double ControlRate,
    double TreatmentRate,
    double AbsoluteDifference,
    double? RelativeLift,
    double ZScore,
    double PValue,
    double ConfidenceIntervalLow,
    double ConfidenceIntervalHigh);

/// <summary>
/// Frequentist two-proportion z-test. Deliberately simple and dependency-free; it assumes a fixed-horizon
/// analysis (decide the sample size up front), so repeatedly peeking at p-values inflates false positives.
/// </summary>
public static class Statistics
{
    private const double Z95 = 1.959963984540054;

    public static ProportionComparison? CompareProportions(long controlUsers, long controlConversions, long treatmentUsers, long treatmentConversions)
    {
        if (controlUsers <= 0 || treatmentUsers <= 0)
        {
            return null;
        }

        var p1 = (double)controlConversions / controlUsers;
        var p2 = (double)treatmentConversions / treatmentUsers;
        var diff = p2 - p1;

        // Pooled standard error for the hypothesis test (H0: p1 == p2).
        var pooled = (double)(controlConversions + treatmentConversions) / (controlUsers + treatmentUsers);
        var sePooled = Math.Sqrt(pooled * (1 - pooled) * ((1.0 / controlUsers) + (1.0 / treatmentUsers)));
        var z = sePooled > 0 ? diff / sePooled : 0;
        var pValue = sePooled > 0 ? 2 * (1 - NormalCdf(Math.Abs(z))) : 1;

        // Unpooled standard error for the confidence interval of the difference.
        var se = Math.Sqrt((p1 * (1 - p1) / controlUsers) + (p2 * (1 - p2) / treatmentUsers));

        return new ProportionComparison(
            p1,
            p2,
            diff,
            p1 > 0 ? diff / p1 : null,
            z,
            Math.Clamp(pValue, 0, 1),
            diff - (Z95 * se),
            diff + (Z95 * se));
    }

    public static double NormalCdf(double x) => 0.5 * (1 + Erf(x / Math.Sqrt(2)));

    /// <summary>Abramowitz &amp; Stegun 7.1.26 (max error 1.5e-7) — plenty for reporting p-values.</summary>
    public static double Erf(double x)
    {
        const double a1 = 0.254829592;
        const double a2 = -0.284496736;
        const double a3 = 1.421413741;
        const double a4 = -1.453152027;
        const double a5 = 1.061405429;
        const double p = 0.3275911;

        var sign = Math.Sign(x);
        x = Math.Abs(x);
        var t = 1 / (1 + (p * x));
        var polynomial = ((((((((a5 * t) + a4) * t) + a3) * t) + a2) * t) + a1) * t;
        var y = 1 - (polynomial * Math.Exp(-x * x));
        return sign * y;
    }
}
