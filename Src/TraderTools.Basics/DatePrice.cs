using System;

namespace TraderTools.Basics
{
    public class DatePrice
    {
        public DatePrice(DateTime date, decimal? price)
        {
            Date = date;
            Price = price;
        }

        public DatePrice()
        {
        }

        public DateTime Date { get; set; }
        public decimal? Price { get; set; }

        public override string ToString()
        {
            return $"{Date} {Price}";
        }
    }
}