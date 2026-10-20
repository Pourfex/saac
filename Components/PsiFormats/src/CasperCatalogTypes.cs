// <copyright file="CasperCatalogTypes.cs" company="SAAC">
// Licensed under the CeCILL-C License. See LICENSE.md file in the project root for full license information.
// This software is distributed under the CeCILL-C FREE SOFTWARE LICENSE AGREEMENT.
// See https://cecill.info/licences/Licence_CeCILL-C_V1-en.html for details.
// </copyright>

namespace SAAC.PsiFormats
{
    /// <summary>
    /// Battery catalogue type persisted by Casper Unity / Server stores (matches historical SaacAnalysisCasper PsiFormats layout).
    /// </summary>
    public struct PsiBatterie
    {
        public PsiBatterie(int id, int tension, int places, bool regulated, int[] modules, string state, float dist)
        {
            this.Id = id;
            this.Tension = tension;
            this.Places = places;
            this.Regulated = regulated;
            this.Modules = modules;
            this.State = state;
            this.Dist = dist;
        }

        public int Id { get; set; }

        public int Tension { get; set; }

        public int Places { get; set; }

        public bool Regulated { get; set; }

        public int[] Modules { get; set; }

        public string State { get; set; }

        public float Dist { get; set; }
    }

    /// <summary>Simple battery event from Casper catalogue streams.</summary>
    public struct PsiBatteryEvent
    {
        public PsiBatteryEvent(int batteryId, int power, int places, bool regulated)
        {
            this.BatteryId = batteryId;
            this.Power = power;
            this.Places = places;
            this.Regulated = regulated;
        }

        public int BatteryId { get; set; }

        public int Power { get; set; }

        public int Places { get; set; }

        public bool Regulated { get; set; }
    }

    /// <summary>Battery finish summary from Casper catalogue streams.</summary>
    public struct PsiBatteryFinish
    {
        public PsiBatteryFinish(
            bool negativeBorn,
            bool positiveBorn,
            bool frontBorn,
            bool backBorn,
            bool onlyTwoBorns,
            int completedSpaces,
            int totalSpaces,
            int[] givenVoltages,
            int[] voltagesRequired,
            int matchVoltages,
            bool regulated)
        {
            this.NegativeBorn = negativeBorn;
            this.PositiveBorn = positiveBorn;
            this.FrontBorn = frontBorn;
            this.BackBorn = backBorn;
            this.OnlyTwoBorns = onlyTwoBorns;
            this.CompletedSpaces = completedSpaces;
            this.TotalSpaces = totalSpaces;
            this.GivenVoltages = givenVoltages;
            this.VoltagesRequired = voltagesRequired;
            this.MatchVoltages = matchVoltages;
            this.Regulated = regulated;
        }

        public bool NegativeBorn { get; set; }

        public bool PositiveBorn { get; set; }

        public bool FrontBorn { get; set; }

        public bool BackBorn { get; set; }

        public bool OnlyTwoBorns { get; set; }

        public int CompletedSpaces { get; set; }

        public int TotalSpaces { get; set; }

        public int[] GivenVoltages { get; set; }

        public int[] VoltagesRequired { get; set; }

        public int MatchVoltages { get; set; }

        public bool Regulated { get; set; }
    }

    /// <summary>Battery module event from Casper catalogue streams.</summary>
    public struct PsiBatteryModuleEvent
    {
        public PsiBatteryModuleEvent(
            int batteryId,
            int power,
            int places,
            bool regulated,
            int moduleId,
            string moduleType,
            int modulePower,
            int placeIndex,
            string moduleStatus)
        {
            this.BatteryId = batteryId;
            this.Power = power;
            this.Places = places;
            this.Regulated = regulated;
            this.ModuleId = moduleId;
            this.ModuleType = moduleType;
            this.ModulePower = modulePower;
            this.PlaceIndex = placeIndex;
            this.ModuleStatus = moduleStatus;
        }

        public int BatteryId { get; set; }

        public int Power { get; set; }

        public int Places { get; set; }

        public bool Regulated { get; set; }

        public int ModuleId { get; set; }

        public string ModuleType { get; set; }

        public int ModulePower { get; set; }

        public int PlaceIndex { get; set; }

        public string ModuleStatus { get; set; }
    }

    /// <summary>Gaze-on-object event from Casper catalogue streams.</summary>
    public struct PsiGazeObjectEvent
    {
        public PsiGazeObjectEvent(int userId, int objectId, bool isGazing, string objectType)
        {
            this.UserId = userId;
            this.ObjectId = objectId;
            this.IsGazing = isGazing;
            this.ObjectType = objectType;
        }

        public int UserId { get; set; }

        public int ObjectId { get; set; }

        public bool IsGazing { get; set; }

        public string ObjectType { get; set; }
    }

    /// <summary>TV / message catalogue type from Casper stores.</summary>
    public struct PsiTV
    {
        public PsiTV(int id, int val1, int val2, string message)
        {
            this.Id = id;
            this.Val1 = val1;
            this.Val2 = val2;
            this.Message = message;
        }

        public int Id { get; set; }

        public int Val1 { get; set; }

        public int Val2 { get; set; }

        public string Message { get; set; }
    }
}
