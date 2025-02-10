using System;
using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;

namespace Sculpt2D
{
    public class Sculpt2DInfo : GH_AssemblyInfo
    {
        public override string Name => "Sculpt2D";

        //Return a 24x24 pixel bitmap to represent this GHA library.
        public override Bitmap Icon => null;

        //Return a short string describing the purpose of this GHA library.
        public override string Description => "";

        public override Guid Id => new Guid("02fd2870-820c-475d-8893-c1ae3ff21f33");

        //Return a string identifying you or your company.
        public override string AuthorName => "";

        //Return a string representing your preferred contact details.
        public override string AuthorContact => "";
    }
}