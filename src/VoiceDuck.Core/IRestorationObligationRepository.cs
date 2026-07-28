namespace VoiceDuck.Core;

public interface IRestorationObligationRepository
{
    RestorationObligationLoadResult LoadAll();

    void SaveAll(IReadOnlyList<RestorationObligation> obligations);

    void DeleteAll();
}
