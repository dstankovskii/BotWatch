namespace BotWatch.Incidents.Domain;

// итог дня по проверке. сырые результаты живут 30 дней, а это - сколько нужно статус-странице
public class DailyUptime
{
    public Guid CheckId { get; private set; }

    // день в UTC
    public DateOnly Day { get; private set; }

    public int Total { get; private set; }

    public int Failed { get; private set; }

    public int AvgDurationMs { get; private set; }

    private DailyUptime()
    {
    }
}
