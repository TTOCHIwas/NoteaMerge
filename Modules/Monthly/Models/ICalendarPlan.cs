using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Notea.Modules.Monthly.Models
{
    public interface ICalendarPlan
    {
        public int PlanId { get; set; }
        string Title { get; set; }
        string Description { get; set; }
        DateTime? StartDate { get; set; }
        DateTime? EndDate { get; set; }
        public bool IsDday { get; set; }
        public string Color { get; set; }
    }
}
