using System;
using static NobndlLog;
using static NobndlUtil;

partial class NobndlLoaderPlugin
{
    private int CountPendingPatches()
    {
        int count=0;
        for (int i=0; i<pendingPylonPatches.Count; i++)
        {
            if (pendingPylonPatches[i]!=null && !pendingPylonPatches[i].Applied)
                count++;
        }
        return count;
    }
}
