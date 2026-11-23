using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;

namespace ActFloorCustomizer;

/// <summary>
/// Lets the player re-roll which room types appear on a stretched map.
///
/// The four sliders are percentages of the rooms that may still change type (all fixed rooms keep
/// their vanilla role: the first row is combat, the row seven above the boss is treasure, the boss
/// row is a rest site). Whatever is left over becomes a normal fight, so the five numbers always
/// add up to 100.
///
/// The vanilla pacing rules are kept: no two elites/rests/shops in a row, rests only from row 5 and
/// never in the last three rows, elites only from row 6, at most one of those per row, and at most
/// 14 elites like the vanilla budget cap.
/// </summary>
public static class RoomMix
{
    public const int MaxPercent = 100;
    public const int EliteCap = 14;

    public readonly record struct Weights(int Unknown, int Shop, int Rest, int Elite, int Monster)
    {
        public int Special => Unknown + Shop + Rest + Elite;
        public int Total => Special + Monster;
        public static Weights None => new(0, 0, 0, 0, 100);
        public bool Custom => Special > 0;
    }

    /// <summary>Percentages straight from the settings, normalised so the total never exceeds 100.</summary>
    public static Weights Current() =>
        Normalise(ModEntry.Settings.UnknownPercent, ModEntry.Settings.ShopPercent,
                  ModEntry.Settings.RestPercent, ModEntry.Settings.ElitePercent);

    public static Weights Normalise(int unknown, int shop, int rest, int elite)
    {
        unknown = Math.Clamp(unknown, 0, MaxPercent);
        shop = Math.Clamp(shop, 0, MaxPercent);
        rest = Math.Clamp(rest, 0, MaxPercent);
        elite = Math.Clamp(elite, 0, MaxPercent);

        int special = unknown + shop + rest + elite;
        if (special > MaxPercent)
        {
            // Over 100: share the room proportionally so no setting is silently dropped.
            double factor = (double)MaxPercent / special;
            unknown = (int)Math.Round(unknown * factor, MidpointRounding.AwayFromZero);
            shop = (int)Math.Round(shop * factor, MidpointRounding.AwayFromZero);
            rest = (int)Math.Round(rest * factor, MidpointRounding.AwayFromZero);
            elite = (int)Math.Round(elite * factor, MidpointRounding.AwayFromZero);
            special = unknown + shop + rest + elite;
            while (special > MaxPercent)
            {
                // Rounding can overshoot by one or two; take it off the largest value.
                if (unknown >= shop && unknown >= rest && unknown >= elite && unknown > 0) { unknown--; }
                else if (shop >= rest && shop >= elite && shop > 0) { shop--; }
                else if (rest >= elite && rest > 0) { rest--; }
                else if (elite > 0) { elite--; }
                else break;
                special = unknown + shop + rest + elite;
            }
        }

        return new Weights(unknown, shop, rest, elite, MaxPercent - special);
    }

    /// <summary>
    /// Re-rolls the room types of a freshly generated map. Returns the number of rooms that are no
    /// longer a normal fight, or 0 when the feature is off or the act is not stretched.
    /// </summary>
    public static int Apply(ActMap? map, ActModel? act, ulong seed) =>
        Apply(map, act, new Rng(seed, "room_mix"));

    /// <summary>Same, with an RNG the caller already derived from the map's own stream.</summary>
    public static int Apply(ActMap? map, ActModel? act, Rng rng)
    {
        try
        {
            if (map == null || act == null) return 0;
            // Vanilla maps keep their vanilla mix; the feature is part of the extra floors.
            if (RunSettings.ForAct(act) <= 0) return 0;

            Weights weights = RunSettings.MixForAct(act);
            if (!weights.Custom) return 0;

            int rows = map.GetRowCount();
            List<MapPoint> points = map.GetAllMapPoints()
                .Where(p => p.CanBeModified)
                .Where(p => p.PointType is MapPointType.Monster or MapPointType.Unknown
                    or MapPointType.Shop or MapPointType.RestSite or MapPointType.Elite)
                .OrderBy(p => p.coord.row)
                .ThenBy(p => p.coord.col)
                .ToList();
            if (points.Count == 0) return 0;

            // Start from a clean slate so the pacing rules are judged against this pass only.
            foreach (MapPoint point in points) point.PointType = MapPointType.Monster;

            foreach (MapPoint point in points)
            {
                MapPointType type = Roll(rng, weights);
                if (type == MapPointType.Monster) continue;
                if (!Allowed(type, point, rows, map))
                {
                    type = FirstAllowed(point, rows, map, weights);
                    if (type == MapPointType.Monster) continue;
                }
                point.PointType = type;
            }

            LimitElites(points);
            EnsureOne(points, rows, map, weights, MapPointType.Shop, rng);
            EnsureOne(points, rows, map, weights, MapPointType.RestSite, rng);

            int changed = points.Count(p => p.PointType != MapPointType.Monster);
            MapLayout.Diag($"mix: rows={rows} unknown={weights.Unknown} shop={weights.Shop} rest={weights.Rest} elite={weights.Elite} monster={weights.Monster} changed={changed}");
            return changed;
        }
        catch (Exception ex)
        {
            MapLayout.Diag("mix: failed " + ex.GetType().Name + ": " + ex.Message);
            return 0;
        }
    }

    private static MapPointType Roll(Rng rng, Weights w)
    {
        int roll = rng.NextInt(0, MaxPercent);
        if (roll < w.Elite) return MapPointType.Elite;
        roll -= w.Elite;
        if (roll < w.Rest) return MapPointType.RestSite;
        roll -= w.Rest;
        if (roll < w.Shop) return MapPointType.Shop;
        roll -= w.Shop;
        if (roll < w.Unknown) return MapPointType.Unknown;
        return MapPointType.Monster;
    }

    private static MapPointType FirstAllowed(MapPoint point, int rows, ActMap map, Weights w)
    {
        // Try the rarer special rooms first; fall back to a normal fight.
        if (w.Elite > 0 && Allowed(MapPointType.Elite, point, rows, map)) return MapPointType.Elite;
        if (w.Rest > 0 && Allowed(MapPointType.RestSite, point, rows, map)) return MapPointType.RestSite;
        if (w.Shop > 0 && Allowed(MapPointType.Shop, point, rows, map)) return MapPointType.Shop;
        if (w.Unknown > 0 && Allowed(MapPointType.Unknown, point, rows, map)) return MapPointType.Unknown;
        return MapPointType.Monster;
    }

    // Mirrors the vanilla rules in StandardActMap without needing the private tables.
    private static bool Allowed(MapPointType type, MapPoint point, int rows, ActMap map)
    {
        int row = point.coord.row;
        switch (type)
        {
            case MapPointType.RestSite:
                return row >= 5 && row < rows - 3
                    && !NeighbourHas(point, MapPointType.RestSite)
                    && !RowHas(map, point, MapPointType.RestSite);
            case MapPointType.Elite:
                return row >= 6
                    && !NeighbourHas(point, MapPointType.Elite)
                    && !RowHas(map, point, MapPointType.Elite);
            case MapPointType.Shop:
                return !NeighbourHas(point, MapPointType.Shop)
                    && !RowHas(map, point, MapPointType.Shop);
            case MapPointType.Unknown:
            case MapPointType.Monster:
                return true;
            default:
                return false;
        }
    }

    private static bool NeighbourHas(MapPoint point, MapPointType type)
    {
        foreach (MapPoint parent in point.parents)
            if (parent.PointType == type) return true;
        foreach (MapPoint child in point.Children)
            if (child.PointType == type) return true;
        return false;
    }

    private static bool RowHas(ActMap map, MapPoint point, MapPointType type)
    {
        foreach (MapPoint other in map.GetAllMapPoints())
        {
            if (other.coord.row != point.coord.row || ReferenceEquals(other, point)) continue;
            if (other.PointType == type) return true;
        }
        return false;
    }

    private static void LimitElites(List<MapPoint> points)
    {
        List<MapPoint> elites = points.Where(p => p.PointType == MapPointType.Elite).ToList();
        int extra = elites.Count - EliteCap;
        if (extra <= 0) return;
        // Keep the elites that are spread out; the others go back to normal fights.
        for (int i = elites.Count - 1; i >= 0 && extra > 0; i--, extra--)
            elites[i].PointType = MapPointType.Monster;
    }

    private static void EnsureOne(List<MapPoint> points, int rows, ActMap map, Weights w, MapPointType type, Rng rng)
    {
        int wanted = type == MapPointType.Shop ? w.Shop : w.Rest;
        if (wanted <= 0) return;
        if (points.Any(p => p.PointType == type)) return;
        List<MapPoint> candidates = points
            .Where(p => p.PointType == MapPointType.Monster && Allowed(type, p, rows, map))
            .ToList();
        if (candidates.Count == 0) return;
        candidates[rng.NextInt(0, candidates.Count)].PointType = type;
    }
}
