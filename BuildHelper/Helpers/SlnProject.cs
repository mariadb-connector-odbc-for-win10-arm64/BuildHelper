using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BuildHelper.Helpers
{
    /// <summary>
    /// `Project("{8BC9CEB8-8B4A-11D0-8D11-00A0C91BC942}") = "ALL_BUILD", "ALL_BUILD.vcxproj", "{86DB31C6-B393-3617-A8FD-885B2E605A2D}"`
    /// </summary>
    record SlnProject(string Id, string Name, string File, string ProjKey, string Line);
}
