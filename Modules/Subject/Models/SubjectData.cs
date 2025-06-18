using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Notea.Modules.Subject.Models
{
    public class SubjectData
    {
        public int SubjectId { get; set; }
        public string SubjectName { get; set; }
        public int TotalStudyTimeSeconds { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime LastModifiedDate { get; set; }
    }
}
