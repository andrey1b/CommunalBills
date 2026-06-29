namespace PrivatUtilities;

// Платёжный сервис (банк или агрегатор) для вкладки «Другой банк»
public class BankService
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public override string ToString() => Name; // для отображения в ComboBox

    public static List<BankService> All() => new()
    {
        // Банки (веб-банкинг)
        new() { Name = "Ощадбанк — Ощад 24/7",     Url = "https://online.oschadbank.ua/" },
        new() { Name = "ПУМБ Online",               Url = "https://online.pumb.ua/" },
        new() { Name = "Raiffeisen — MyRaif",       Url = "https://my.raiffeisen.ua/" },
        new() { Name = "Sense Bank (Альфа) Online", Url = "https://online.sensebank.com.ua/" },
        new() { Name = "monobank (web)",            Url = "https://web.monobank.ua/" },
        new() { Name = "Укрсиббанк — UKRSIB online",Url = "https://my.ukrsibbank.com/" },
        // Агрегаторы (оплата картой без привязки к банку)
        new() { Name = "iPay — оплата картой",      Url = "https://www.ipay.ua/" },
        new() { Name = "EasyPay",                   Url = "https://easypay.ua/" },
        new() { Name = "Portmone",                  Url = "https://www.portmone.com.ua/" },
    };
}
