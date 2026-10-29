using PriceRadar.Core.Normalization;

namespace PriceRadar.Seeder;

internal sealed record CatalogBook(string Title, string Author, string NormTitle, string NormAuthor, decimal MedianPrice);

/// <summary>
/// Book titles for the synthetic dataset: a few real classics (public bibliographic facts) followed by titles built
/// from a Serbian word grammar. Every (author, title) pair is unique after normalisation, as dw.Book requires.
/// </summary>
internal static class SyntheticCatalog
{
    private static readonly (string Title, string Author)[] Classics =
    [
        ("Na Drini ćuprija", "Ivo Andrić"), ("Prokleta avlija", "Ivo Andrić"), ("Travnička hronika", "Ivo Andrić"),
        ("Gospođica", "Ivo Andrić"), ("Derviš i smrt", "Meša Selimović"), ("Tvrđava", "Meša Selimović"),
        ("Seobe", "Miloš Crnjanski"), ("Seobe 2", "Miloš Crnjanski"), ("Roman o Londonu", "Miloš Crnjanski"),
        ("Grobnica za Borisa Davidoviča", "Danilo Kiš"), ("Bašta, pepeo", "Danilo Kiš"), ("Peščanik", "Danilo Kiš"),
        ("Enciklopedija mrtvih", "Danilo Kiš"), ("Hazarski rečnik", "Milorad Pavić"), ("Predeo slikan čajem", "Milorad Pavić"),
        ("Kad su cvetale tikve", "Dragoslav Mihailović"), ("Bašta sljezove boje", "Branko Ćopić"), ("Orlovi rano lete", "Branko Ćopić"),
        ("Gorski vijenac", "Petar Petrović Njegoš"), ("Nečista krv", "Borisav Stanković"), ("Pop Ćira i pop Spira", "Stevan Sremac"),
        ("Zona Zamfirova", "Stevan Sremac"), ("Sumnjivo lice", "Branislav Nušić"), ("Gospođa ministarka", "Branislav Nušić"),
        ("Pesme", "Desanka Maksimović"), ("Pesme", "Branko Radičević"), ("Kako upokojiti vampira", "Borislav Pekić"),
        ("Besnilo", "Borislav Pekić"), ("Vreme čuda", "Borislav Pekić"), ("Kiklop", "Ranko Marinković"),
        ("Zločin i kazna", "Fjodor Dostojevski"), ("Braća Karamazovi", "Fjodor Dostojevski"), ("Ana Karenjina", "Lav Tolstoj"),
        ("Majstor i Margarita", "Mihail Bulgakov"), ("Sto godina samoće", "Gabrijel Garsija Markes"),
        ("Mali princ", "Antoan de Sent Egziperi"), ("Alhemičar", "Paulo Koeljo"),
    ];

    private static readonly string[] Adjectives =
    [
        "Tihi", "Crni", "Beli", "Zlatni", "Poslednji", "Prvi", "Daleki", "Izgubljeni", "Tajni", "Stari", "Mladi", "Večni",
        "Gorki", "Slatki", "Hladni", "Plavi", "Crveni", "Zeleni", "Tamni", "Svetli", "Duboki", "Visoki", "Usamljeni",
        "Divlji", "Čudni", "Tužni", "Srećni", "Nemi", "Mračni", "Krhki",
    ];

    private static readonly string[] Nouns =
    [
        "grad", "most", "reka", "noć", "dan", "put", "san", "vetar", "kamen", "hleb", "prozor", "vrt", "bunar", "toranj",
        "zvono", "ogledalo", "pismo", "dnevnik", "putnik", "ratnik", "pesnik", "kralj", "vuk", "orao", "konj", "brod",
        "ostrvo", "planina", "dolina", "šuma", "zima", "leto", "jesen", "proleće", "more", "sunce", "mesec", "zvezda",
        "pesma", "priča",
    ];

    private static readonly string[] FirstNames =
    [
        "Milan", "Dragan", "Jelena", "Marija", "Nikola", "Stefan", "Ana", "Milica", "Đorđe", "Vesna", "Zoran", "Goran",
        "Ljiljana", "Nenad", "Snežana", "Branislav", "Dušan", "Jovana", "Aleksandar", "Tamara", "Vladimir", "Ivana",
        "Predrag", "Svetlana", "Miroslav", "Dragana", "Bojan", "Katarina", "Radomir", "Mirjana", "Slobodan", "Gordana",
        "Žarko", "Biljana", "Vuk", "Teodora", "Lazar", "Olivera", "Ćirilo", "Nada",
    ];

    private static readonly string[] Surnames =
    [
        "Petrović", "Jovanović", "Nikolić", "Marković", "Đorđević", "Stojanović", "Ilić", "Stanković", "Pavlović",
        "Milošević", "Kovačević", "Todorović", "Živković", "Janković", "Mitrović", "Popović", "Radović", "Lazić",
        "Savić", "Tomić", "Kostić", "Obradović", "Vasić", "Simić", "Ristić", "Đukić", "Lukić", "Babić", "Krstić",
        "Filipović", "Božić", "Mladenović", "Stevanović", "Antić", "Zdravković", "Gajić", "Ćirić", "Novaković",
        "Perić", "Šarić", "Vuković", "Milić", "Matić", "Dimitrijević", "Bogdanović", "Maksimović", "Ivanović",
        "Aleksić", "Jakovljević", "Pešić", "Lalić", "Mihajlović", "Nedeljković", "Spasić", "Trifunović", "Urošević",
        "Veljković", "Zorić", "Čolić", "Džinić",
    ];

    public static List<CatalogBook> Generate(Random rng, int count)
    {
        var books = new List<CatalogBook>(count);
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (title, author) in Classics.Take(count))
        {
            Add(title, author);
        }

        while (books.Count < count)
        {
            var title = rng.Next(10) switch
            {
                < 4 => $"{Pick(Adjectives)} {Pick(Nouns)}",
                < 7 => $"{Pick(Adjectives)} {Pick(Nouns)} i {Pick(Nouns)}",
                < 9 => $"{Capitalize(Pick(Nouns))} na {Pick(Nouns)}",
                _ => $"{Pick(Adjectives)} {Pick(Nouns)} {rng.Next(2, 4)}",   // a numbered volume
            };
            Add(title, $"{Pick(FirstNames)} {Pick(Surnames)}");
        }

        return books;

        string Pick(string[] words) => words[rng.Next(words.Length)];

        void Add(string title, string author)
        {
            // The same normaliser as MatchJob and the lookup endpoint, so seeded books match real listings exactly.
            var normTitle = TextNormalizer.NormalizeTitle(title);
            var normAuthor = TextNormalizer.NormalizeAuthor(author);
            if (keys.Add($"{normAuthor}|{normTitle}"))
            {
                var median = Math.Round((decimal)Math.Exp(Math.Log(300) + (rng.NextDouble() * (Math.Log(2500) - Math.Log(300)))) / 10m) * 10m;
                books.Add(new CatalogBook(title, author, normTitle, normAuthor, median));
            }
        }
    }

    private static string Capitalize(string word) => char.ToUpperInvariant(word[0]) + word[1..];
}
