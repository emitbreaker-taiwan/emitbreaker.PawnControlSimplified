using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace emitbreaker.PawnControl
{
    [Flags]
    public enum PawnIdentityFlags
    {
        None = 0,
        IsColonist = 1 << 0,
        IsPrisoner = 1 << 1,
        IsSlave = 1 << 2,
        IsGuest = 1 << 3,
        IsAnimal = 1 << 4,
        IsHumanlike = 1 << 5,
        IsWildMan = 1 << 6,
        IsQuestLodger = 1 << 7,
        IsMechanoid = 1 << 8,
        IsSlaveOfColony = 1 << 9,
        IsPrisonerOfColony = 1 << 10,
        IsMutant = 1 << 11,

        // Composite for convenience
        IsPawn = IsColonist | IsPrisoner | IsSlave | IsGuest | IsHumanlike | IsAnimal,
        IsPlayable = IsColonist | IsPrisoner | IsSlave | IsHumanlike,
        IsAnyColonistLike = IsColonist | IsHumanlike | IsSlaveOfColony | IsPrisonerOfColony,
        IsAnyAnimalLike = IsAnimal,
        IsTemporary = IsQuestLodger | IsGuest,
    }
}
