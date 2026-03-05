namespace Transfermarkt.Domain;

/// <summary>
/// Country using official FIFA country codes.
/// </summary>
[PublicAPI]
public readonly record struct Country {
    // ==================== CAF (Africa) - 54 members ====================
    public static readonly Country Algeria                = new("ALG", "Algeria", Continent.Africa);
    public static readonly Country Angola                 = new("ANG", "Angola", Continent.Africa);
    public static readonly Country Benin                  = new("BEN", "Benin", Continent.Africa);
    public static readonly Country Botswana               = new("BOT", "Botswana", Continent.Africa);
    public static readonly Country BurkinaFaso            = new("BFA", "Burkina Faso", Continent.Africa);
    public static readonly Country Burundi                = new("BDI", "Burundi", Continent.Africa);
    public static readonly Country Cameroon               = new("CMR", "Cameroon", Continent.Africa);
    public static readonly Country CapeVerde              = new("CPV", "Cape Verde Islands", Continent.Africa);
    public static readonly Country CentralAfricanRepublic = new("CTA", "Central African Republic", Continent.Africa);
    public static readonly Country Chad                   = new("CHA", "Chad", Continent.Africa);
    public static readonly Country Comoros                = new("COM", "Comoros Islands", Continent.Africa);
    public static readonly Country Congo                  = new("CGO", "Congo", Continent.Africa);
    public static readonly Country CongoDR                = new("COD", "Congo DR", Continent.Africa);
    public static readonly Country CoteDIvoire            = new("CIV", "Côte d'Ivoire", Continent.Africa);
    public static readonly Country Djibouti               = new("DJI", "Djibouti", Continent.Africa);
    public static readonly Country Egypt                  = new("EGY", "Egypt", Continent.Africa);
    public static readonly Country EquatorialGuinea       = new("EQG", "Equatorial Guinea", Continent.Africa);
    public static readonly Country Eritrea                = new("ERI", "Eritrea", Continent.Africa);
    public static readonly Country Ethiopia               = new("ETH", "Ethiopia", Continent.Africa);
    public static readonly Country Gabon                  = new("GAB", "Gabon", Continent.Africa);
    public static readonly Country Gambia                 = new("GAM", "Gambia", Continent.Africa);
    public static readonly Country Ghana                  = new("GHA", "Ghana", Continent.Africa);
    public static readonly Country Guinea                 = new("GUI", "Guinea", Continent.Africa);
    public static readonly Country GuineaBissau           = new("GNB", "Guinea-Bissau", Continent.Africa);
    public static readonly Country Kenya                  = new("KEN", "Kenya", Continent.Africa);
    public static readonly Country Lesotho                = new("LES", "Lesotho", Continent.Africa);
    public static readonly Country Liberia                = new("LBR", "Liberia", Continent.Africa);
    public static readonly Country Libya                  = new("LBY", "Libya", Continent.Africa);
    public static readonly Country Madagascar             = new("MAD", "Madagascar", Continent.Africa);
    public static readonly Country Malawi                 = new("MWI", "Malawi", Continent.Africa);
    public static readonly Country Mali                   = new("MLI", "Mali", Continent.Africa);
    public static readonly Country Mauritania             = new("MTN", "Mauritania", Continent.Africa);
    public static readonly Country Mauritius              = new("MRI", "Mauritius", Continent.Africa);
    public static readonly Country Morocco                = new("MAR", "Morocco", Continent.Africa);
    public static readonly Country Mozambique             = new("MOZ", "Mozambique", Continent.Africa);
    public static readonly Country Namibia                = new("NAM", "Namibia", Continent.Africa);
    public static readonly Country Niger                  = new("NIG", "Niger", Continent.Africa);
    public static readonly Country Nigeria                = new("NGA", "Nigeria", Continent.Africa);
    public static readonly Country Rwanda                 = new("RWA", "Rwanda", Continent.Africa);
    public static readonly Country SaoTomeAndPrincipe     = new("STP", "São Tomé e Príncipe", Continent.Africa);
    public static readonly Country Senegal                = new("SEN", "Senegal", Continent.Africa);
    public static readonly Country Seychelles             = new("SEY", "Seychelles", Continent.Africa);
    public static readonly Country SierraLeone            = new("SLE", "Sierra Leone", Continent.Africa);
    public static readonly Country Somalia                = new("SOM", "Somalia", Continent.Africa);
    public static readonly Country SouthAfrica            = new("RSA", "South Africa", Continent.Africa);
    public static readonly Country SouthSudan             = new("SSD", "South Sudan", Continent.Africa);
    public static readonly Country Sudan                  = new("SDN", "Sudan", Continent.Africa);
    public static readonly Country Eswatini               = new("SWZ", "Eswatini", Continent.Africa);
    public static readonly Country Tanzania               = new("TAN", "Tanzania", Continent.Africa);
    public static readonly Country Togo                   = new("TOG", "Togo", Continent.Africa);
    public static readonly Country Tunisia                = new("TUN", "Tunisia", Continent.Africa);
    public static readonly Country Uganda                 = new("UGA", "Uganda", Continent.Africa);
    public static readonly Country Zambia                 = new("ZAM", "Zambia", Continent.Africa);
    public static readonly Country Zimbabwe               = new("ZIM", "Zimbabwe", Continent.Africa);

    // ==================== AFC (Asia) - 47 members ====================
    public static readonly Country Afghanistan            = new("AFG", "Afghanistan", Continent.Asia);
    public static readonly Country Australia              = new("AUS", "Australia", Continent.Asia);
    public static readonly Country Bahrain                = new("BHR", "Bahrain", Continent.Asia);
    public static readonly Country Bangladesh             = new("BAN", "Bangladesh", Continent.Asia);
    public static readonly Country Bhutan                 = new("BHU", "Bhutan", Continent.Asia);
    public static readonly Country Brunei                 = new("BRU", "Brunei Darussalam", Continent.Asia);
    public static readonly Country Cambodia               = new("CAM", "Cambodia", Continent.Asia);
    public static readonly Country China                  = new("CHN", "China PR", Continent.Asia);
    public static readonly Country ChineseTaipei          = new("TPE", "Chinese Taipei", Continent.Asia);
    public static readonly Country EastTimor              = new("TLS", "East Timor", Continent.Asia);
    public static readonly Country HongKong               = new("HKG", "Hong Kong", Continent.Asia);
    public static readonly Country India                  = new("IND", "India", Continent.Asia);
    public static readonly Country Indonesia              = new("IDN", "Indonesia", Continent.Asia);
    public static readonly Country Iran                   = new("IRN", "Iran", Continent.Asia);
    public static readonly Country Iraq                   = new("IRQ", "Iraq", Continent.Asia);
    public static readonly Country Japan                  = new("JPN", "Japan", Continent.Asia);
    public static readonly Country Jordan                 = new("JOR", "Jordan", Continent.Asia);
    public static readonly Country NorthKorea             = new("PRK", "Korea DPR", Continent.Asia);
    public static readonly Country SouthKorea             = new("KOR", "Korea Republic", Continent.Asia);
    public static readonly Country Kuwait                 = new("KUW", "Kuwait", Continent.Asia);
    public static readonly Country Kyrgyzstan             = new("KGZ", "Kyrgyzstan", Continent.Asia);
    public static readonly Country Laos                   = new("LAO", "Laos", Continent.Asia);
    public static readonly Country Lebanon                = new("LBN", "Lebanon", Continent.Asia);
    public static readonly Country Malaysia               = new("MAS", "Malaysia", Continent.Asia);
    public static readonly Country Maldives               = new("MDV", "Maldives", Continent.Asia);
    public static readonly Country Mongolia               = new("MGL", "Mongolia", Continent.Asia);
    public static readonly Country Myanmar                = new("MYA", "Myanmar", Continent.Asia);
    public static readonly Country Nepal                  = new("NEP", "Nepal", Continent.Asia);
    public static readonly Country Oman                   = new("OMA", "Oman", Continent.Asia);
    public static readonly Country Pakistan               = new("PAK", "Pakistan", Continent.Asia);
    public static readonly Country Palestine              = new("PAL", "Palestine", Continent.Asia);
    public static readonly Country Philippines            = new("PHI", "Philippines", Continent.Asia);
    public static readonly Country Qatar                  = new("QAT", "Qatar", Continent.Asia);
    public static readonly Country SaudiArabia            = new("KSA", "Saudi Arabia", Continent.Asia);
    public static readonly Country Singapore              = new("SGP", "Singapore", Continent.Asia);
    public static readonly Country SriLanka               = new("SRI", "Sri Lanka", Continent.Asia);
    public static readonly Country Syria                  = new("SYR", "Syria", Continent.Asia);
    public static readonly Country Tajikistan             = new("TJK", "Tajikistan", Continent.Asia);
    public static readonly Country Thailand               = new("THA", "Thailand", Continent.Asia);
    public static readonly Country Turkmenistan           = new("TKM", "Turkmenistan", Continent.Asia);
    public static readonly Country UnitedArabEmirates     = new("UAE", "United Arab Emirates", Continent.Asia);
    public static readonly Country Uzbekistan             = new("UZB", "Uzbekistan", Continent.Asia);
    public static readonly Country Vietnam                = new("VIE", "Vietnam", Continent.Asia);
    public static readonly Country Yemen                  = new("YEM", "Yemen", Continent.Asia);

    // ==================== UEFA (Europe) - 55 members ====================
    public static readonly Country Albania                = new("ALB", "Albania", Continent.Europe);
    public static readonly Country Andorra                = new("AND", "Andorra", Continent.Europe);
    public static readonly Country Armenia                = new("ARM", "Armenia", Continent.Europe);
    public static readonly Country Austria                = new("AUT", "Austria", Continent.Europe);
    public static readonly Country Azerbaijan             = new("AZE", "Azerbaijan", Continent.Europe);
    public static readonly Country Belarus                = new("BLR", "Belarus", Continent.Europe);
    public static readonly Country Belgium                = new("BEL", "Belgium", Continent.Europe);
    public static readonly Country BosniaHerzegovina      = new("BIH", "Bosnia Herzegovina", Continent.Europe);
    public static readonly Country Bulgaria               = new("BUL", "Bulgaria", Continent.Europe);
    public static readonly Country Croatia                = new("CRO", "Croatia", Continent.Europe);
    public static readonly Country Cyprus                 = new("CYP", "Cyprus", Continent.Europe);
    public static readonly Country Czechia                = new("CZE", "Czech Republic", Continent.Europe);
    public static readonly Country Denmark                = new("DEN", "Denmark", Continent.Europe);
    public static readonly Country England                = new("ENG", "England", Continent.Europe);
    public static readonly Country Estonia                = new("EST", "Estonia", Continent.Europe);
    public static readonly Country FaroeIslands           = new("FRO", "Faroe Islands", Continent.Europe);
    public static readonly Country Finland                = new("FIN", "Finland", Continent.Europe);
    public static readonly Country France                 = new("FRA", "France", Continent.Europe);
    public static readonly Country Georgia                = new("GEO", "Georgia", Continent.Europe);
    public static readonly Country Germany                = new("GER", "Germany", Continent.Europe);
    public static readonly Country Gibraltar              = new("GIB", "Gibraltar", Continent.Europe);
    public static readonly Country Greece                 = new("GRE", "Greece", Continent.Europe);
    public static readonly Country Netherlands            = new("NED", "Netherlands", Continent.Europe);
    public static readonly Country Hungary                = new("HUN", "Hungary", Continent.Europe);
    public static readonly Country Iceland                = new("ISL", "Iceland", Continent.Europe);
    public static readonly Country Israel                 = new("ISR", "Israel", Continent.Europe);
    public static readonly Country Italy                  = new("ITA", "Italy", Continent.Europe);
    public static readonly Country Kazakhstan             = new("KAZ", "Kazakhstan", Continent.Europe);
    public static readonly Country Kosovo                 = new("KVX", "Kosovo", Continent.Europe);
    public static readonly Country Latvia                 = new("LVA", "Latvia", Continent.Europe);
    public static readonly Country Liechtenstein          = new("LIE", "Liechtenstein", Continent.Europe);
    public static readonly Country Lithuania              = new("LTU", "Lithuania", Continent.Europe);
    public static readonly Country Luxembourg             = new("LUX", "Luxembourg", Continent.Europe);
    public static readonly Country NorthMacedonia         = new("MKD", "North Macedonia", Continent.Europe);
    public static readonly Country Malta                  = new("MLT", "Malta", Continent.Europe);
    public static readonly Country Moldova                = new("MDA", "Moldova", Continent.Europe);
    public static readonly Country Montenegro             = new("MNE", "Montenegro", Continent.Europe);
    public static readonly Country NorthernIreland        = new("NIR", "Northern Ireland", Continent.Europe);
    public static readonly Country Norway                 = new("NOR", "Norway", Continent.Europe);
    public static readonly Country Poland                 = new("POL", "Poland", Continent.Europe);
    public static readonly Country Portugal               = new("POR", "Portugal", Continent.Europe);
    public static readonly Country Ireland                = new("IRL", "Republic of Ireland", Continent.Europe);
    public static readonly Country Romania                = new("ROU", "Romania", Continent.Europe);
    public static readonly Country Russia                 = new("RUS", "Russia", Continent.Europe);
    public static readonly Country SanMarino              = new("SMR", "San Marino", Continent.Europe);
    public static readonly Country Scotland               = new("SCO", "Scotland", Continent.Europe);
    public static readonly Country Serbia                 = new("SRB", "Serbia", Continent.Europe);
    public static readonly Country Slovakia               = new("SVK", "Slovakia", Continent.Europe);
    public static readonly Country Slovenia               = new("SVN", "Slovenia", Continent.Europe);
    public static readonly Country Spain                  = new("ESP", "Spain", Continent.Europe);
    public static readonly Country Sweden                 = new("SWE", "Sweden", Continent.Europe);
    public static readonly Country Switzerland            = new("SUI", "Switzerland", Continent.Europe);
    public static readonly Country Turkey                 = new("TUR", "Turkey", Continent.Europe);
    public static readonly Country Ukraine                = new("UKR", "Ukraine", Continent.Europe);
    public static readonly Country Wales                  = new("WAL", "Wales", Continent.Europe);

    // ==================== CONCACAF (North/Central America & Caribbean) - 41 members ====================
    public static readonly Country Anguilla               = new("AIA", "Anguilla", Continent.NorthAmerica);
    public static readonly Country AntiguaAndBarbuda      = new("ATG", "Antigua and Barbuda", Continent.NorthAmerica);
    public static readonly Country Aruba                  = new("ARU", "Aruba", Continent.NorthAmerica);
    public static readonly Country Bahamas                = new("BAH", "Bahamas", Continent.NorthAmerica);
    public static readonly Country Barbados               = new("BRB", "Barbados", Continent.NorthAmerica);
    public static readonly Country Belize                 = new("BLZ", "Belize", Continent.NorthAmerica);
    public static readonly Country Bermuda                = new("BER", "Bermuda", Continent.NorthAmerica);
    public static readonly Country BritishVirginIslands   = new("VGB", "British Virgin Islands", Continent.NorthAmerica);
    public static readonly Country Canada                 = new("CAN", "Canada", Continent.NorthAmerica);
    public static readonly Country CaymanIslands          = new("CAY", "Cayman Islands", Continent.NorthAmerica);
    public static readonly Country CostaRica              = new("CRC", "Costa Rica", Continent.NorthAmerica);
    public static readonly Country Cuba                   = new("CUB", "Cuba", Continent.NorthAmerica);
    public static readonly Country Curacao                = new("CUW", "Curaçao", Continent.NorthAmerica);
    public static readonly Country Dominica               = new("DMA", "Dominica", Continent.NorthAmerica);
    public static readonly Country DominicanRepublic      = new("DOM", "Dominican Republic", Continent.NorthAmerica);
    public static readonly Country ElSalvador             = new("SLV", "El Salvador", Continent.NorthAmerica);
    public static readonly Country Grenada                = new("GRN", "Grenada", Continent.NorthAmerica);
    public static readonly Country Guatemala              = new("GUA", "Guatemala", Continent.NorthAmerica);
    public static readonly Country Guyana                 = new("GUY", "Guyana", Continent.NorthAmerica);
    public static readonly Country Haiti                  = new("HAI", "Haiti", Continent.NorthAmerica);
    public static readonly Country Honduras               = new("HON", "Honduras", Continent.NorthAmerica);
    public static readonly Country Jamaica                = new("JAM", "Jamaica", Continent.NorthAmerica);
    public static readonly Country Mexico                 = new("MEX", "Mexico", Continent.NorthAmerica);
    public static readonly Country Montserrat             = new("MSR", "Montserrat", Continent.NorthAmerica);
    public static readonly Country Nicaragua              = new("NCA", "Nicaragua", Continent.NorthAmerica);
    public static readonly Country Panama                 = new("PAN", "Panama", Continent.NorthAmerica);
    public static readonly Country PuertoRico             = new("PUR", "Puerto Rico", Continent.NorthAmerica);
    public static readonly Country SaintKittsAndNevis     = new("SKN", "Saint Kitts and Nevis", Continent.NorthAmerica);
    public static readonly Country SaintLucia             = new("LCA", "Saint Lucia", Continent.NorthAmerica);
    public static readonly Country SaintVincent           = new("VIN", "Saint Vincent and the Grenadines", Continent.NorthAmerica);
    public static readonly Country Suriname               = new("SUR", "Suriname", Continent.NorthAmerica);
    public static readonly Country TrinidadAndTobago      = new("TRI", "Trinidad and Tobago", Continent.NorthAmerica);
    public static readonly Country TurksAndCaicos         = new("TCA", "Turks and Caicos Islands", Continent.NorthAmerica);
    public static readonly Country UnitedStates           = new("USA", "United States", Continent.NorthAmerica);
    public static readonly Country USVirginIslands        = new("VIR", "United States Virgin Islands", Continent.NorthAmerica);

    // ==================== OFC (Oceania) - 11 members ====================
    public static readonly Country AmericanSamoa          = new("ASA", "American Samoa", Continent.Oceania);
    public static readonly Country CookIslands            = new("COK", "Cook Islands", Continent.Oceania);
    public static readonly Country Fiji                   = new("FIJ", "Fiji", Continent.Oceania);
    public static readonly Country NewCaledonia           = new("NCL", "New Caledonia", Continent.Oceania);
    public static readonly Country NewZealand             = new("NZL", "New Zealand", Continent.Oceania);
    public static readonly Country PapuaNewGuinea         = new("PNG", "Papua New Guinea", Continent.Oceania);
    public static readonly Country Samoa                  = new("SAM", "Samoa", Continent.Oceania);
    public static readonly Country SolomonIslands         = new("SOL", "Solomon Islands", Continent.Oceania);
    public static readonly Country Tahiti                 = new("TAH", "Tahiti", Continent.Oceania);
    public static readonly Country Tonga                  = new("TGA", "Tonga", Continent.Oceania);
    public static readonly Country Vanuatu                = new("VAN", "Vanuatu", Continent.Oceania);

    // ==================== CONMEBOL (South America) - 10 members ====================
    public static readonly Country Argentina              = new("ARG", "Argentina", Continent.SouthAmerica);
    public static readonly Country Bolivia                = new("BOL", "Bolivia", Continent.SouthAmerica);
    public static readonly Country Brazil                 = new("BRA", "Brazil", Continent.SouthAmerica);
    public static readonly Country Chile                  = new("CHI", "Chile", Continent.SouthAmerica);
    public static readonly Country Colombia               = new("COL", "Colombia", Continent.SouthAmerica);
    public static readonly Country Ecuador                = new("ECU", "Ecuador", Continent.SouthAmerica);
    public static readonly Country Paraguay               = new("PAR", "Paraguay", Continent.SouthAmerica);
    public static readonly Country Peru                   = new("PER", "Peru", Continent.SouthAmerica);
    public static readonly Country Uruguay                = new("URU", "Uruguay", Continent.SouthAmerica);
    public static readonly Country Venezuela              = new("VEN", "Venezuela", Continent.SouthAmerica);

    /// <summary>
    /// Represents an unknown country.
    /// </summary>
    public static readonly Country Unknown = new("UNK", "Unknown", Continent.Unknown);

    /// <summary>
    /// All CAF (Africa) member countries.
    /// </summary>
    public static readonly IReadOnlyList<Country> Africa = [
        Algeria, Angola, Benin, Botswana, BurkinaFaso, Burundi, Cameroon, CapeVerde, CentralAfricanRepublic,
        Chad, Comoros, Congo, CongoDR, CoteDIvoire, Djibouti, Egypt, EquatorialGuinea, Eritrea, Ethiopia,
        Gabon, Gambia, Ghana, Guinea, GuineaBissau, Kenya, Lesotho, Liberia, Libya, Madagascar, Malawi,
        Mali, Mauritania, Mauritius, Morocco, Mozambique, Namibia, Niger, Nigeria, Rwanda, SaoTomeAndPrincipe,
        Senegal, Seychelles, SierraLeone, Somalia, SouthAfrica, SouthSudan, Sudan, Eswatini, Tanzania,
        Togo, Tunisia, Uganda, Zambia, Zimbabwe
    ];

    /// <summary>
    /// All AFC (Asia) member countries.
    /// </summary>
    public static readonly IReadOnlyList<Country> Asia = [
        Afghanistan, Australia, Bahrain, Bangladesh, Bhutan, Brunei, Cambodia, China, ChineseTaipei,
        EastTimor, HongKong, India, Indonesia, Iran, Iraq, Japan, Jordan, NorthKorea, SouthKorea, Kuwait,
        Kyrgyzstan, Laos, Lebanon, Malaysia, Maldives, Mongolia, Myanmar, Nepal, Oman, Pakistan, Palestine,
        Philippines, Qatar, SaudiArabia, Singapore, SriLanka, Syria, Tajikistan, Thailand, Turkmenistan,
        UnitedArabEmirates, Uzbekistan, Vietnam, Yemen
    ];

    /// <summary>
    /// All UEFA (Europe) member countries.
    /// </summary>
    public static readonly IReadOnlyList<Country> Europe = [
        Albania, Andorra, Armenia, Austria, Azerbaijan, Belarus, Belgium, BosniaHerzegovina, Bulgaria,
        Croatia, Cyprus, Czechia, Denmark, England, Estonia, FaroeIslands, Finland, France, Georgia,
        Germany, Gibraltar, Greece, Netherlands, Hungary, Iceland, Israel, Italy, Kazakhstan, Kosovo,
        Latvia, Liechtenstein, Lithuania, Luxembourg, NorthMacedonia, Malta, Moldova, Montenegro,
        NorthernIreland, Norway, Poland, Portugal, Ireland, Romania, Russia, SanMarino, Scotland,
        Serbia, Slovakia, Slovenia, Spain, Sweden, Switzerland, Turkey, Ukraine, Wales
    ];

    /// <summary>
    /// All CONCACAF (North/Central America &amp; Caribbean) member countries.
    /// </summary>
    public static readonly IReadOnlyList<Country> NorthAmerica = [
        Anguilla, AntiguaAndBarbuda, Aruba, Bahamas, Barbados, Belize, Bermuda, BritishVirginIslands,
        Canada, CaymanIslands, CostaRica, Cuba, Curacao, Dominica, DominicanRepublic, ElSalvador,
        Grenada, Guatemala, Guyana, Haiti, Honduras, Jamaica, Mexico, Montserrat, Nicaragua, Panama,
        PuertoRico, SaintKittsAndNevis, SaintLucia, SaintVincent, Suriname, TrinidadAndTobago,
        TurksAndCaicos, UnitedStates, USVirginIslands
    ];

    /// <summary>
    /// All OFC (Oceania) member countries.
    /// </summary>
    public static readonly IReadOnlyList<Country> Oceania = [
        AmericanSamoa, CookIslands, Fiji, NewCaledonia, NewZealand, PapuaNewGuinea, Samoa,
        SolomonIslands, Tahiti, Tonga, Vanuatu
    ];

    /// <summary>
    /// All CONMEBOL (South America) member countries.
    /// </summary>
    public static readonly IReadOnlyList<Country> SouthAmerica = [
        Argentina, Bolivia, Brazil, Chile, Colombia, Ecuador, Paraguay, Peru, Uruguay, Venezuela
    ];

    /// <summary>
    /// All defined countries (209 FIFA members).
    /// </summary>
    public static readonly IReadOnlyList<Country> All =
        [..Africa, ..Asia, ..Europe, ..NorthAmerica, ..Oceania, ..SouthAmerica];

    static readonly Dictionary<string, Country> ByCode = All.ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);

    public Country(string code, string name, Continent continent) {
        Code      = code;
        Name      = name;
        Continent = continent;
    }

    /// <summary>
    /// Official FIFA country code (e.g., "BRA", "GER", "ESP").
    /// </summary>
    public string Code { get; init; }

    /// <summary>
    /// Full country name.
    /// </summary>
    public string Name { get; init; }

    /// <summary>
    /// The continent this country belongs to.
    /// </summary>
    public Continent Continent { get; init; }

    /// <summary>
    /// Whether this is an unknown country.
    /// </summary>
    public bool IsUnknown => this == Unknown;

    /// <summary>
    /// Gets a country by its FIFA code.
    /// </summary>
    public static Country? FromCode(string code) => ByCode.TryGetValue(code, out var country) ? country : null;

    /// <summary>
    /// Gets all countries in a continent.
    /// </summary>
    public static IEnumerable<Country> InContinent(Continent continent) => All.Where(c => c.Continent == continent);

    /// <summary>
    /// Creates a custom country not in the predefined list.
    /// </summary>
    public static Country Custom(string code, string name, Continent continent) => new(code, name, continent);

    public override string ToString() => Name;
}

/// <summary>
/// Geographic continent.
/// </summary>
[PublicAPI]
public enum Continent {
    Unknown,
    Africa,
    Asia,
    Europe,
    NorthAmerica,
    Oceania,
    SouthAmerica
}
