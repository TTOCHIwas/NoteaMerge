using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading.Tasks;

namespace Notea.Modules.Subject.ViewModels
{
    public class NoteLine
    {
        public int Index { get; set; }
        public string Content { get; set; }
        public string ContentType { get; set; } = "text";
        public string ImageUrl { get; set; }

        public int DisplayOrder { get; set; }

        public int CategoryId { get; set; }

        public int TextId { get; set; }

        public int SubjectId { get; set; }

    }
}
