using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Notea.Modules.Monthly.Models
{
    public class MonthlyPlan : ICalendarPlan
    {
        public int PlanId { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public bool IsDday { get; set; }
        public string Color { get; set; }
    }
}
