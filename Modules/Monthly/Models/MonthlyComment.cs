using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Notea.Modules.Monthly.Models
{
    public class MonthlyComment : IMonthlyComment
    {
        public string? Comment { get; set; }
        public DateTime? MonthDate { get; set; }
    }
}
