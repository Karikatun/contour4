using System.Collections.Generic;

namespace Contour4.Procedural.Catalog
{
    public static class ModuleCatalog
    {
        public static IEnumerable<GrayModule> Rooms()
        {
            yield return StorageCupboard.Create();
            yield return CalibrationSector.Create();
            yield return SampleStorage.Create();
            yield return StraightCorridor.Create();
            yield return StraightCorridor.Create(longForm: true);
            yield return ElbowCorridor.Create();
            yield return JunctionCorridor.Create();
            yield return ObservationLab.Create();
            yield return UpperExitCorridor.Create();
            yield return ShaftPlant.Create();
            yield return SampleStorage.Create(narrow: true);
            yield return ObservationLab.Create(brokenFloor: true);
            yield return QuarterTurnBridge.Create();
            yield return SpiralCorridor.Create();
            yield return OpposedChambers.Create();
            yield return OpposedChambers.Create(floorToFloor: true);
            yield return RightAngleChambers.Create();
            yield return UShapeShaft.Create();
            yield return GalleryChamber.Create();
            yield return NicheChamber.Create();
            yield return MaintenanceTrench.Create();
        }
    }
}
