﻿using ETHBot.DataLayer.Data.Enums;
using ETHBot.DataLayer.Data.ETH.Food;
using ETHDINFKBot.Data;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace ETHDINFKBot.Helpers
{
    public enum MealTime
    {
        Breakfast = 0,
        Lunch = 1,
        Dinner = 2
    }

    public enum Language
    {
        English = 0,
        German = 1
    }

    /*
        public class Menu
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public string MultilineDescription { get; set; }
            public string FirstLine { get; set; }
            public decimal Price { get; set; }
            public bool IsVegan { get; set; }
            public bool IsVegetarian { get; set; }
            public bool IsLocal { get; set; }
            public bool IsBalanced { get; set; }
            public bool IsGlutenFree { get; set; }
            public bool IsLactoseFree { get; set; }
            public string ImgUrl { get; set; }
        }*/

    public class FoodHelper
    {
        private static GoogleEngine Google = new GoogleEngine();

        private static FoodDBManager FoodDBManager = FoodDBManager.Instance();

        private readonly ILogger _logger = new Logger<FoodHelper>(Program.Logger);

        public void LoadMenus(int restaurantId = -1, bool fixOnly = false)
        {
            var avilableRestaurants = FoodDBManager.GetAllRestaurants();

            // flip order of restaurants to load UZH first
            avilableRestaurants.Reverse();

            Google = new GoogleEngine(); // TODO Better reset

            foreach (var restaurant in avilableRestaurants)
            {
                if (restaurantId > -1 && restaurant.RestaurantId != restaurantId)
                    continue; // Skip this restaurant

                if (restaurant.IsOpen)
                {
                    if (fixOnly)
                    {
                        var allMenus = FoodDBManager.GetMenusByDay(
                            DateTime.Now,
                            restaurant.RestaurantId
                        );
                        if (allMenus.Count != 0)
                            continue; // We have some menus loaded do not reload
                    }

                    //await Context.Channel.SendMessageAsync($"Processing {restaurant.Name}");

                    List<Menu> menu = new List<Menu>();
                    switch (restaurant.Location)
                    {
                        case RestaurantLocation.None:
                            break;

                        // SV Restaurant handler
                        case RestaurantLocation.ETH_Zentrum:
                        case RestaurantLocation.ETH_Hoengg:

                            // for sv restaurant go only into fix only mode because selenium would nuke me atm
                            var allMenus = FoodDBManager.GetMenusByDay(
                                DateTime.Now,
                                restaurant.RestaurantId
                            );
                            if (allMenus.Count != 0)
                                continue; // We have some menus loaded do not reload

                            var today = DateTime.UtcNow.Date;

                            List<DateTime> remainingWeekdays = new List<DateTime>();

                            remainingWeekdays.Add(today);

                            for (int i = 0; i < 5; i++)
                            {
                                today = today.AddDays(1);
                                if (
                                    today.DayOfWeek == DayOfWeek.Saturday
                                    || today.DayOfWeek == DayOfWeek.Sunday
                                )
                                    break;

                                remainingWeekdays.Add(today);
                            }

                            foreach (var day in remainingWeekdays)
                            {
                                var svRestaurantMenuInfos = GetSVRestaurantMenu(
                                    restaurant.MenuUrl,
                                    day
                                );

                                if (svRestaurantMenuInfos == null)
                                    continue;
                                //await Context.Channel.SendMessageAsync($"Found for {restaurant.Name}: {svRestaurantMenuInfos.Count} menus");

                                foreach (var svRestaurantMenu in svRestaurantMenuInfos)
                                {
                                    try
                                    {
                                        svRestaurantMenu.Menu.RestaurantId =
                                            restaurant.RestaurantId; // Link the menu to restaurant

                                        var menuImage = GetImageForFood(svRestaurantMenu.Menu);

                                        // Set image
                                        if (menuImage != null)
                                            svRestaurantMenu.Menu.MenuImageId =
                                                menuImage.MenuImageId;

                                        var dbMenu = FoodDBManager.CreateMenu(
                                            svRestaurantMenu.Menu
                                        );

                                        // Link menu with allergies
                                        foreach (var allergyId in svRestaurantMenu.AllergyIds)
                                        {
                                            FoodDBManager.CreateMenuAllergy(
                                                svRestaurantMenu.Menu.MenuId,
                                                allergyId
                                            );
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogError("Exception while loading SV menu: ", ex);
                                        Console.WriteLine(ex);
                                    }
                                }
                            }
                            break;

                        // ZFV Restaurant handler
                        case RestaurantLocation.UZH_Zentrum:
                        case RestaurantLocation.UZH_Irchel:

                            if (restaurant.IsFood2050Supported)
                            {
                                try
                                {
                                    var menus = GetUZHMensaMenuWeek(
                                        restaurant.InternalName,
                                        restaurant.AdditionalInternalName,
                                        restaurant.TimeParameter
                                    );

                                    foreach (var currentMenu in menus)
                                    {
                                        try
                                        {
                                            currentMenu.RestaurantId = restaurant.RestaurantId; // Link the menu to restaurant

                                            var dbMenu = FoodDBManager.CreateMenu(currentMenu);

                                            // Link menu with allergies for now no allergies handling
                                            /*foreach (var allergyId in menu.AllergyIds)
                                            {
                                                FoodDBManager.CreateMenuAllergy(menu.MenuId, allergyId);
                                            }*/
                                        }
                                        catch (Exception ex)
                                        {
                                            _logger.LogError(
                                                "Exception while loading UZH menu: ",
                                                ex
                                            );
                                            Console.WriteLine(ex);
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError("Exception while loading UZH menu: ", ex);
                                    Console.WriteLine(ex);
                                }
                                continue;
                            }

                            try
                            {
                                var uzhMenuInfos = GetUzhMenus(
                                    GetUZHDayOfTheWeek(),
                                    restaurant.InternalName
                                );

                                if (uzhMenuInfos == null)
                                    continue;
                                //await Context.Channel.SendMessageAsync($"Found for {restaurant.Name}: {uzhMenuInfos.Count} menus");

                                foreach (var uzhMenuInfo in uzhMenuInfos)
                                {
                                    try
                                    {
                                        uzhMenuInfo.Menu.RestaurantId = restaurant.RestaurantId; // Link the menu to restaurant

                                        var menuImage = GetImageForFood(uzhMenuInfo.Menu);

                                        // Set image
                                        if (menuImage != null)
                                            uzhMenuInfo.Menu.MenuImageId = menuImage.MenuImageId;

                                        var dbMenu = FoodDBManager.CreateMenu(uzhMenuInfo.Menu);

                                        // Link menu with allergies
                                        foreach (var allergyId in uzhMenuInfo.AllergyIds)
                                        {
                                            FoodDBManager.CreateMenuAllergy(dbMenu.MenuId, allergyId);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogError("Exception while loading UZH menu: ", ex);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError("Exception while loading UZH menu from HTTP: ", ex);
                            }

                            break;
                        default:
                            break;
                    }
                }
                else
                {
                    //await Context.Channel.SendMessageAsync($"{restaurant.Name} is closed");
                }
            }
            //await Context.Channel.SendMessageAsync($"Done");
        }

        /*
                public static List<ETHBot.DataLayer.Data.ETH.Food.Restaurant> FetchETHRestaurant()
                {
                    List<ETHBot.DataLayer.Data.ETH.Food.Restaurant> allRestaurants = new List<ETHBot.DataLayer.Data.ETH.Food.Restaurant>();

                    try
                    {
                        string ethRestaurantsListLink = "https://ethz.ch/en/campus/getting-to-know/cafes-restaurants-shops/gastronomy/restaurants-and-cafeterias/";

                        Dictionary<int, string> ethRestaurantLocations = new Dictionary<int, string>();
                        ethRestaurantLocations.Add(1, "zentrum.html");
                        ethRestaurantLocations.Add(2, "hoenggerberg.html");

                        foreach (var location in ethRestaurantLocations)
                        {
                            WebClient client = new WebClient();
                            string html = client.DownloadString(ethRestaurantsListLink + location.Value);


                            var allrestaurantsReturned = HandleEthRestaurantLocationInfo(html, location.Key);
                            allRestaurants.AddRange(allrestaurantsReturned);
                        }
                    }
                    catch (Exception ex)
                    {
                        int i = 1;
                    }

                    return allRestaurants;
                }

                private static List<ETHBot.DataLayer.Data.ETH.Food.Restaurant> HandleEthRestaurantLocationInfo(string html, int locationId)
                {
                    string xPath = "//a[@class='eth-link has-icon-before']";
                    string baseUrl = "https://ethz.ch";

                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);

                    HtmlNodeCollection nodes = doc.DocumentNode.SelectNodes(xPath);

                    WebClient client = new WebClient();
                    List<ETHBot.DataLayer.Data.ETH.Food.Restaurant> restaurants = new List<ETHBot.DataLayer.Data.ETH.Food.Restaurant>();

                    foreach (var node in nodes)
                    {
                        string link = node.Attributes["href"].Value;

                        if(!link.StartsWith("http"))
                            link = baseUrl + link;

                        string htmlRestaurant = client.DownloadString(link);

                        var restaurant = HandleEthRestaurant(htmlRestaurant, locationId);
                        restaurants.Add(restaurant);
                    }

                    return restaurants;
                }

                private static ETHBot.DataLayer.Data.ETH.Food.Restaurant HandleEthRestaurant(string html, int locationId)
                {
                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);

                    ETHBot.DataLayer.Data.ETH.Food.Restaurant currentRestaurant = new ETHBot.DataLayer.Data.ETH.Food.Restaurant();

                    currentRestaurant.Name = doc.DocumentNode.SelectSingleNode("//h1[@class='donthyphenate']").InnerText;
                    currentRestaurant.Location = locationId;
                    // TODO opening times parsing
                    // TODO check if it has benus


                    return currentRestaurant;
                }*/

        // TODO Provide list of ids to search
        public List<(Menu Menu, List<int> AllergyIds)> GetSVRestaurantMenu(
            string link,
            DateTime dateTime
        )
        {
            try
            {
                WebClient client = new WebClient();

                string html = "";

                try
                {
                    html = client.DownloadString(link);
                }
                catch (Exception ex)
                {
                    // TODO Log
                    return null; // Likely a HTTP Error
                }

                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                List<(Menu, List<int>)> menus = new List<(Menu, List<int>)>();

                for (int i = 1; i <= 5; i++)
                {
                    try
                    {
                        var dateNode = doc.DocumentNode.SelectSingleNode(
                            $"//*[@for=\"mp-tab{i}\"]"
                        );
                        var dateString = dateNode.InnerText
                            .Replace("\t", "")
                            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

                        var dateToSearch = $"{dateTime:dd.MM.}";

                        if (dateString.Last().Trim() != dateToSearch)
                            continue; // The day is not correct likely a sunday showing monday menus

                        // [0] has day Mo, Di
                        // [1] has the date


                        var menuMainNode = doc.DocumentNode.SelectSingleNode(
                            $"//*[@id=\"menu-plan-tab{i}\"]"
                        );

                        var menusDoc = new HtmlDocument();
                        menusDoc.LoadHtml(menuMainNode.InnerHtml);

                        var menuItems = menusDoc.DocumentNode.SelectNodes(
                            "//*[@class=\"menu-item\"]"
                        );

                        foreach (var item in menuItems)
                        {
                            var menuDoc = new HtmlDocument();
                            menuDoc.LoadHtml(item.InnerHtml);

                            Menu currentMenu = new Menu();

                            currentMenu.Name = menuDoc.DocumentNode
                                .SelectSingleNode("//*[@class=\"menuline\"]")
                                ?.InnerText;

                            string title = menuDoc.DocumentNode
                                .SelectSingleNode("//*[@class=\"menu-title\"]")
                                .InnerText;
                            title = HttpUtility.HtmlDecode(title);

                            bool isClausiusBar = false; // Clausius bar uses no proper food titles or just a broken menu which sv restaurant doesnt care about
                            if (string.IsNullOrWhiteSpace(currentMenu.Name))
                            {
                                isClausiusBar = true;
                                currentMenu.Name = title;
                            }

                            string description = menuDoc.DocumentNode
                                .SelectSingleNode("//*[@class=\"menu-description\"]")
                                .InnerText;
                            description = HttpUtility.HtmlDecode(description);

                            currentMenu.Description = description;
                            if (!isClausiusBar)
                                currentMenu.Description = title + Environment.NewLine + description;

                            if (currentMenu.Description.Trim().ToLower().StartsWith("geschlossen"))
                                continue; // because fuck polymensa and their stupid inconsistency

                            if (currentMenu.Description.ToLower().Contains("beachten sie"))
                                continue; // TODO Handle this maybe better but screw polymensa tbh for them being lazy and inconsistent

                            var priceStringNode = menuDoc.DocumentNode.SelectNodes(
                                "//*[@class=\"price\"]"
                            );

                            string priceString = "";
                            if (priceStringNode != null)
                                priceString =
                                    priceStringNode.FirstOrDefault()?.InnerText.Replace("\t", "")
                                    ?? ""; // polymensa incapable to put prices on for whatever reason
                            else
                                priceString = "-1"; // sometimes they dont have prices on the site

                            double price = -1;

                            if (priceString.Contains("STUD"))
                            {
                                // TODO Exception handling
                                price = double.Parse(
                                    priceString
                                        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                                        .FirstOrDefault(),
                                    NumberStyles.Any,
                                    CultureInfo.InvariantCulture
                                );
                            }
                            else
                            {
                                // Dozentenfoyer (works same as above case maybe the check isnt needed for now)
                                price = double.Parse(
                                    priceString
                                        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                                        .FirstOrDefault(),
                                    NumberStyles.Any,
                                    CultureInfo.InvariantCulture
                                );
                            }

                            currentMenu.Amount = price; // TODO Dozentenfoyer has no student prices

                            if (
                                menuDoc.DocumentNode
                                    .SelectSingleNode("//*[@class=\"menu-labels\"]")
                                    ?.InnerHtml.ToLower()
                                    .Contains("vegetarian") == true
                            )
                                currentMenu.IsVegetarian = true;

                            if (
                                menuDoc.DocumentNode
                                    .SelectSingleNode("//*[@class=\"menu-labels\"]")
                                    ?.InnerHtml.ToLower()
                                    .Contains("vegan") == true
                            )
                                currentMenu.IsVegan = true;

                            string allergiesString =
                                menuDoc.DocumentNode
                                    .SelectSingleNode("//*[@class=\"allergen-info\"]")
                                    ?.InnerText.Replace("\t", "")
                                    .Trim() ?? "";

                            allergiesString = HttpUtility.HtmlDecode(allergiesString);
                            if (allergiesString.Contains(":"))
                                allergiesString =
                                    allergiesString
                                        .Split(":", StringSplitOptions.RemoveEmptyEntries)
                                        .LastOrDefault() ?? "";

                            var allergyIdList = new List<int>();

                            if (allergiesString.Contains(","))
                            {
                                var AllergyIds = allergiesString.Split(
                                    ",",
                                    StringSplitOptions.RemoveEmptyEntries
                                );

                                foreach (var AllergyIdString in AllergyIds)
                                {
                                    if (int.TryParse(AllergyIdString.Trim(), out int AllergyId))
                                    {
                                        // TODO Save the ids
                                        allergyIdList.Add(AllergyId);
                                    }
                                    else { }
                                }
                            }

                            currentMenu.DateTime = dateTime; //.AddDays(-1);
                            menus.Add((currentMenu, allergyIdList));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            $"Error while loading SV Restaurant: {link}. With: {ex.Message} for index {i}",
                            ex
                        );
                    }
                }

                return menus;
            }
            catch (Exception ex)
            {
                // TODO handle this per menu, becaus polymensa is just broken
                // TODO maybe change to ethz menu site
                _logger.LogError(
                    $"Error while loading SV Restaurant: {link}. With: {ex.Message}",
                    ex
                );
                return null;
            }
        }

        private List<Menu> GetPolymensaMenu(MealTime mealTime = MealTime.Lunch, int id = 12)
        {
            string xPath = "//*[@class=\"scrollarea-content\"]";

            string lang = "de";
            string dateString = DateTime.Today.ToString("yyyy-MM-dd"); //"2022-03-25";

            int polyMensaId = id;

            WebClient client = new WebClient();

            string html = "";

            try
            {
                html = client.DownloadString(
                    $"https://ethz.ch/de/campus/erleben/gastronomie-und-einkaufen/gastronomie/menueplaene/offerDay.html?language={lang}&id={polyMensaId}&date={dateString}"
                );
            }
            catch (Exception ex)
            {
                // TODO Log
                return null; // Likely a HTTP Error
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            HtmlNodeCollection nodes = doc.DocumentNode.SelectNodes(xPath);

            var mainNode = nodes[0];
            if (mealTime == MealTime.Dinner)
            {
                //if (nodes.Count == 1)
                //return new List<Menu>() { new Menu() { Description = "Index out of size" } };
                mainNode = nodes[1];
            }

            var node = mainNode.ChildNodes.First(i => i.Name == "table");

            var children = node.ChildNodes;

            List<Menu> polymensaMenus = new List<Menu>();
            foreach (var child in children.Where(i => i.Name == "tr"))
            {
                var childNodes = child.ChildNodes.Where(i => i.Name == "td").ToList();

                string priceString = childNodes[2].InnerText;

                // Only for dozentenfoyer (and they cant keep it consistent it seems)
                if (priceString == "NaN" || priceString == "")
                    priceString = childNodes[3].InnerText;

                var menu = new Menu()
                {
                    Description = HttpUtility.HtmlDecode(childNodes[1].InnerText),
                    Name = childNodes[0].InnerText,
                    Amount = double.Parse(priceString) // TODO fix formatting with the comma
                };

                //Check if PolyMensa has menu with useless information
                if (menu.Description == "Dieses Menu servieren wir Ihnen gerne bald wieder!")
                {
                    continue;
                }

                var lines = childNodes[1].ChildNodes
                    .Where(i => i.Name == "strong" || i.Name == "#text")
                    .Select(i => HttpUtility.HtmlDecode(i.InnerText).Replace("\"", ""))
                    .ToList();

                menu.Description = string.Join(" ", lines);
                //menu.MultilineDescription = string.Join("\r\n", lines);// TODO check if atleast 2 lines to begin with
                //menu.FirstLine = lines.First(); // TODO check if atleast 2 lines to begin with
                menu.Description = menu.Description.Replace("&amp;", "&").Trim(); // clean up string

                polymensaMenus.Add(menu);
            }

            foreach (var menu in polymensaMenus)
            {
                //break;
                //await Context.Channel.SendMessageAsync($"**Menu: {menu.Description} Description: {menu.Name} IsVegan:{menu.IsVegan} IsLocal:{menu.IsLocal} Price: {menu.Price}**");

                //var reply = new GoogleEngine().ImageSearch(menu.FirstLine.Replace("\"", "").Trim(), lang: "de");

                // TODO Test if img resolves, if not use 2. result


                /*
                // 70% of the cases show sand because the food is legit dry as sand
                if (menu.Description.ToLower().Contains("couscous") && new Random().Next(0, 10) < 7)
                {
                    menu.ImgUrl = GetImageFromGoogle("Desert Dune", "en");
                }
                else
                {
                    menu.ImgUrl = GetImageFromGoogle(menu.Description, "de");
                    if (menu.ImgUrl == "")
                        menu.ImgUrl = GetImageFromGoogle(menu.FirstLine, "de");

                    // Incase the menu name is in english search as english
                    if (menu.ImgUrl == "")
                        menu.ImgUrl = GetImageFromGoogle(menu.Description, "en");
                    if (menu.ImgUrl == "")
                        menu.ImgUrl = GetImageFromGoogle(menu.FirstLine, "en");

                    if (menu.ImgUrl == "")
                        menu.ImgUrl = Program.Client.CurrentUser.GetAvatarUrl();
                }*/
            }

            return polymensaMenus;
        }

        public List<Menu> GetUZHMensaMenuWeek(string location, string mensa, string time = null)
        {
            try
            {
                WebClient client = new WebClient();
                var menus = new List<Menu>();

                string timeString = time == null ? "" : $"{time}/";

                string mainUrl = $"https://app.food2050.ch/{location}/{mensa}/menu/{timeString}weekly";
                string mainPage = client.DownloadString(mainUrl);

                // get text between "buildId":" and ","
                // TODO make this more robust
                int startIndex = mainPage.IndexOf("\"buildId\":\"") + "\"buildId\":\"".Length;
                string buildIdString = mainPage.Substring(startIndex);
                int endIndex = buildIdString.IndexOf("\",");
                string buildId = buildIdString.Substring(0, endIndex);

                string url =
                    $"https://app.food2050.ch/_next/data/{buildId}/{location}/{mensa}/menu/{timeString}weekly.json";

                string json = client.DownloadString(url);

                var result = JsonConvert.DeserializeObject<Food2050WeeklyResponse>(json);
                var categories = result.pageProps.query.location.kitchen.digitalMenu?.categories ?? new List<Category>();

                foreach (var category in categories)
                {
                    if (category.__typename != "DigitalMenuCategory")
                        continue;

                    foreach (var item in category.items)
                    {
                        foreach (var recipe in item.dailyRecipies)
                        {
                            if (recipe?.recipe == null)
                                continue;

                            // example https://app.food2050.ch/_next/data/fWt87G0z-iWkq_diJzXc_/uzh-zentrum/untere-mensa/food-profile/2023-07-28-mittag-butcher.json?locationSlug=uzh-zentrum&kitchenSlug=untere-mensa&slug=2023-07-28-mittag-butcher
                            var menuUrl = $"https://app.food2050.ch/_next/data/{buildId}/{location}/{mensa}/food-profile/{recipe.recipe.slug}.json";

                            Food2050MenuResponse menuResult = null;

                            try
                            {
                                string menuJson = client.DownloadString(menuUrl);
                                menuResult = JsonConvert.DeserializeObject<Food2050MenuResponse>(menuJson);

                                if (menuResult == null)
                                    continue;
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine(ex);
                                menus.Add(
                                    new Menu()
                                    {
                                        Name = item.displayName,
                                        Description = ex.Message,
                                        DateTime = recipe.date.AddHours(Program.TimeZoneInfo.IsDaylightSavingTime(recipe.date) ? 2 : 1)
                                    }
                                );
                                continue;
                            }

                            var responseRecipe = menuResult.pageProps.recipe;

                            double price = 0;

                            foreach (var priceItem in responseRecipe.prices)
                            {
                                if (priceItem.category.title == "Student")
                                {
                                    price = priceItem.amount;
                                    break;
                                }
                            }

                            var imageUrl = responseRecipe.imageUrl ?? "";

                            var menu = new Menu()
                            {
                                Name = item.displayName,
                                Description = responseRecipe.title_de,
                                Amount = price,
                                IsVegan = responseRecipe.isVegan,
                                IsVegetarian = responseRecipe.isVegetarian,
                                Calories = responseRecipe.energy != null ? (int)responseRecipe.energy : 0,
                                Protein = Math.Round(responseRecipe.protein ?? 0, 1),
                                Carbohydrates = Math.Round(responseRecipe.carbohydrates ?? 0, 1),
                                Fat = Math.Round(responseRecipe.fat ?? 0, 1),
                                Salt = Math.Round(responseRecipe.salt ?? 0, 1),
                                DirectMenuImageUrl = responseRecipe.imageUrl,
                                DateTime = recipe.date.AddHours(
                                    Program.TimeZoneInfo.IsDaylightSavingTime(recipe.date) ? 2 : 1
                                )
                            };

                            if (menu.DirectMenuImageUrl == null)
                            {
                                // find from menus with direct image if there exists one menu with same description
                                menu.FallbackMenuImageUrl =
                                    FoodDBManager.GetDirectImageByMenuDescription(menu.Description);

                                if (string.IsNullOrWhiteSpace(menu.FallbackMenuImageUrl))
                                {
                                    // find from current menu list if any menu has same description
                                    menu.FallbackMenuImageUrl = menus
                                        .FirstOrDefault(x => x.Description == menu.Description)
                                        ?.DirectMenuImageUrl;
                                }
                            }

                            menus.Add(menu);
                            System.Threading.Thread.Sleep(1000);
                        }
                    }
                }

                return menus;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return new List<Menu>();
            }
        }

        public List<(Menu Menu, List<int> AllergyIds)> GetUzhMenus(
            string day,
            string mensa,
            Language language = Language.English
        )
        {
            ///html/body/div[6]/section/div/section/div[2]/div[1]/div/div[2]/div/div/div/div/div/div/div/div/table/tbody[2]
            string xPath = "//*[@id=\"box-1\"]/ul/li/div/div/div";
            // get div with class NewsListItem
            xPath = "//div[@class=\"NewsListItem\"]/div";

            // day = "freitag";
            //string mensa = "zentrum-mercato";

            WebClient client = new WebClient();
            string html = "";

            try
            {
                html = client.DownloadString(
                    $"https://www.mensa.uzh.ch/de/menueplaene/{mensa}/{day}.html"
                );
            }
            catch (Exception ex)
            {
                // TODO Log
                return null; // Likely a HTTP Error
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            HtmlNode node = doc.DocumentNode.SelectSingleNode(xPath);

            List<(Menu, List<int>)> menus = new List<(Menu, List<int>)>();

            int step = 0;
            bool brHitOnce = false;

            string allMenuHtml = node.InnerHtml;

            var menuHtmlList = allMenuHtml.Split("<h3>").Skip(1); // First entry is just a new line;

            foreach (var menuHtml in menuHtmlList)
            {
                Menu currentMenu = new Menu();

                try
                {
                    var menuDoc = new HtmlDocument();
                    menuDoc.LoadHtml("<h3>" + menuHtml); // Add h3 tag as it got removed by split

                    var titleNode = menuDoc.DocumentNode.SelectSingleNode("//h3");

                    currentMenu.Name = titleNode.InnerText.Split('|').FirstOrDefault()?.Trim();

                    if (currentMenu.Name == "News")
                        continue; // Non valid entry

                    string priceField = titleNode.InnerText
                        .Split('|')
                        .LastOrDefault()
                        ?.Trim()
                        .Replace("CHF", "");
                    currentMenu.Amount = double.Parse(
                        priceField.Split('/').First(),
                        NumberStyles.Any,
                        CultureInfo.InvariantCulture
                    );

                    var descriptionNode = menuDoc.DocumentNode.SelectSingleNode("//p[1]");

                    // TODO Proper HTML Encoding/Decoding
                    var descriptionLines = descriptionNode.InnerHtml
                        .Replace("&amp;", "&")
                        .Trim()
                        .Split("<br>", StringSplitOptions.RemoveEmptyEntries)
                        .Where(i => !(i.Contains(":") || i.Contains(";"))) // These lines usually contain info about meat country of origin
                        .Select(i => i.Trim())
                        .ToList();

                    currentMenu.Description = string.Join(
                        Environment.NewLine,
                        descriptionLines /*.Take(lines.Count - 1)*/
                    );

                    var images = menuDoc.DocumentNode.SelectNodes("//img");
                    if (images != null)
                    {
                        foreach (var image in images)
                        {
                            var altValue = image.GetAttributeValue("alt", "");

                            if (altValue == "vegan")
                                currentMenu.IsVegan = true;
                            else if (altValue == "vegetarian")
                                currentMenu.IsVegetarian = true;
                            else if (altValue == "mni_ubp_local")
                                currentMenu.IsLocal = true;
                            else if (altValue == "mni_ebp_enjoy")
                                currentMenu.IsBalanced = true;
                            else if (altValue == "gluten todo")
                                currentMenu.IsGlutenFree = true;
                            else if (altValue == "lactose todo")
                                currentMenu.IsLactoseFree = true;
                        }
                    }

                    var tables = menuDoc.DocumentNode.SelectNodes("//table");
                    if (tables != null)
                    {
                        foreach (var table in tables)
                        {
                            var tableDoc = new HtmlDocument();
                            tableDoc.LoadHtml(table.InnerHtml);

                            if (tableDoc.DocumentNode.InnerText.ToLower().Contains("kcal"))
                            {
                                var tableCells = tableDoc.DocumentNode.SelectNodes("//td");

                                // TODO CHECK TOTAL AMOUNT OF CELLS
                                // 6 total rows
                                // 0 = header
                                // 1 = kcal
                                // 2 = protein
                                // 3 = fat
                                // 4 = carbohydrates
                                // 5 = salt

                                // TODO check if this works properly try catch if it fails
                                var caloriesText = tableCells[1].InnerText;

                                //.SelectSingleNode("//table/tbody/tr[2]/td[2]");
                                //string innerHtmlCalories = tableRows.InnerText;

                                if (caloriesText.Contains("("))
                                {
                                    // Remove ' to parse the string
                                    caloriesText = caloriesText.Replace("'", "");

                                    int start = caloriesText.IndexOf("(");
                                    int end = caloriesText.IndexOf(" kcal");
                                    caloriesText = caloriesText.Substring(
                                        start + 1,
                                        end - start - 1
                                    );

                                    // TODO add logs when it fails to parse
                                    int.TryParse(caloriesText, out int caloriesAmount);

                                    currentMenu.Calories = caloriesAmount;
                                }

                                var proteinText = tableCells[3].InnerText.Trim().Replace("g", "");
                                var fatText = tableCells[5].InnerText.Trim().Replace("g", "");
                                var carbohydratesText = tableCells[7].InnerText
                                    .Trim()
                                    .Replace("g", "");
                                var saltText = tableCells[9].InnerText.Trim().Replace("g", "");

                                // TODO change int to decimal

                                if (
                                    double.TryParse(
                                        proteinText,
                                        NumberStyles.Any,
                                        CultureInfo.InvariantCulture,
                                        out double proteinAmount
                                    )
                                )
                                    currentMenu.Protein = proteinAmount;

                                if (
                                    double.TryParse(
                                        fatText,
                                        NumberStyles.Any,
                                        CultureInfo.InvariantCulture,
                                        out double fatAmount
                                    )
                                )
                                    currentMenu.Fat = fatAmount;

                                if (
                                    double.TryParse(
                                        carbohydratesText,
                                        NumberStyles.Any,
                                        CultureInfo.InvariantCulture,
                                        out double carbsAmount
                                    )
                                )
                                    currentMenu.Carbohydrates = carbsAmount;

                                if (
                                    double.TryParse(
                                        saltText,
                                        NumberStyles.Any,
                                        CultureInfo.InvariantCulture,
                                        out double saltAmount
                                    )
                                )
                                    currentMenu.Salt = saltAmount;
                            }
                        }
                    }

                    var allergyIds = new List<int>();

                    var allergyNode = menuDoc.DocumentNode.SelectNodes("//p").Last();
                    if (
                        allergyNode != null
                        && allergyNode.InnerText.ToLower().Contains("allergikerinformationen")
                    )
                    {
                        string allergiesString = allergyNode.InnerText.Trim();
                        if (allergiesString.Contains(":"))
                            allergiesString =
                                allergiesString
                                    .Split(":", StringSplitOptions.RemoveEmptyEntries)
                                    .LastOrDefault() ?? "";

                        if (allergiesString.Contains(","))
                        {
                            var allergyNames = allergiesString.Split(
                                ",",
                                StringSplitOptions.RemoveEmptyEntries
                            );

                            foreach (var AllergyName in allergyNames)
                            {
                                switch (AllergyName.Trim())
                                {
                                    case "Glutenhaltiges Getreide":
                                        allergyIds.Add(1);
                                        break;

                                    case "Krebstiere":
                                        allergyIds.Add(2);
                                        break;

                                    case "Eier":
                                        allergyIds.Add(3);
                                        break;

                                    case "Fisch":
                                        allergyIds.Add(4);
                                        break;

                                    case "Erdnüsse":
                                        allergyIds.Add(5);
                                        break;

                                    case "Soja":
                                        allergyIds.Add(6);
                                        break;

                                    case "Milch":
                                        allergyIds.Add(7);
                                        break;

                                    case "Hartschalenobst (Nüsse)":
                                        allergyIds.Add(8);
                                        break;

                                    case "Sellerie":
                                        allergyIds.Add(9);
                                        break;

                                    case "Senf":
                                        allergyIds.Add(10);
                                        break;

                                    case "Sesam":
                                        allergyIds.Add(11);
                                        break;

                                    case "Schwefeldioxid und Sulfite":
                                        allergyIds.Add(12);
                                        break;

                                    case "Lupine":
                                        allergyIds.Add(13);
                                        break;

                                    case "Weichtiere":
                                        allergyIds.Add(14);
                                        break;

                                    default:
                                        int i = 1;
                                        break; // Shouldt enter TODO catch
                                }
                            }
                        }
                    }

                    // TODO set permanent fix
                    currentMenu.DateTime = DateTime.Now; //.AddDays(-1);
                    menus.Add((currentMenu, allergyIds));
                }
                catch (Exception ex)
                {
                    // TODO log these errors
                }
            }

            return menus;
        }

        private List<string> GetImageFromGoogle(string text, string lang = "de")
        {
            //return Google.ImageSearch(text.Replace("\"", "").Replace("\"n", "").Trim(), lang: lang).Result;
            return Google
                .GetSearchResultBySelenium(
                    text.Replace("\"", "").Replace("\"n", " ").Trim(),
                    lang: lang
                )
                .Result;
        }

        public MenuImage GetImageForFood(Menu menu, bool fullSearch = false)
        {
            try
            {
                // First query german then english
                var menuImage = GetImageForFood(menu, "de", fullSearch).Result;
                //if (menuImage == null)
                //    menuImage = GetImageForFood(menu, "en").Result;

                return menuImage;
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        // TODO Decide on the order for different mensas
        private async Task<MenuImage> GetImageForFood(
            Menu menu,
            string language,
            bool fullSearch = false
        )
        {
            List<string> images = new List<string>();
            HttpClient client = new HttpClient();

            string searchTerm = menu.Description;
            if (searchTerm.StartsWith("mit"))
                searchTerm = menu.Name + " " + menu.Description;

            searchTerm = searchTerm.Replace("\"", "").Replace("\n", " ").Trim();
            var dbImages = FoodDBManager.GetMenuImages(searchTerm, language);
            if (dbImages.Count > 0)
            {
                return dbImages.First();
            }
            else
            {
                return null; // atm disabled

                var imageLinks = GetImageFromGoogle(searchTerm, language);

                // This is to fix ratelimit hits
                if (fullSearch)
                {
                    if (imageLinks.Count == 0 && !menu.Description.StartsWith("mit"))
                    {
                        searchTerm = menu.Name + " " + menu.Description;
                        searchTerm = searchTerm.Replace("\"", "").Replace("\n", " ").Trim();
                        // Check db first
                        dbImages = FoodDBManager.GetMenuImages(searchTerm, language);
                        if (dbImages.Count > 0)
                            return dbImages.First();

                        imageLinks = GetImageFromGoogle(searchTerm, language);
                    }

                    /*if (imageLinks.Count == 0)
                    {
                        searchTerm = menu.Name + " " + menu.Description;
                        imageLinks = GetImageFromGoogle(searchTerm, language);
                    }*/

                    // Try only first line
                    if (imageLinks.Count == 0 && menu.Description.Contains("\n"))
                    {
                        searchTerm = menu.Description.Split("\n").First();
                        searchTerm = searchTerm.Replace("\"", "").Replace("\n", " ").Trim();
                        // Check db first
                        dbImages = FoodDBManager.GetMenuImages(searchTerm, language);
                        if (dbImages.Count > 0)
                            return dbImages.First();

                        imageLinks = GetImageFromGoogle(searchTerm, language);
                    }

                    if (imageLinks.Count == 0 && menu.Description.Contains(","))
                    {
                        searchTerm = menu.Description.Split(",").First();
                        searchTerm = searchTerm.Replace("\"", "").Replace("\n", " ").Trim();
                        // Check db first
                        dbImages = FoodDBManager.GetMenuImages(searchTerm, language);
                        if (dbImages.Count > 0)
                            return dbImages.First();

                        imageLinks = GetImageFromGoogle(searchTerm, language);
                    }
                }

                //if (imageLinks.Count == 0 && menu.Description.Contains(","))
                //    imageLinks = GetImageFromGoogle(menu.Name + " " + menu.Description.Split(",").First(), language);

                // TODO handle images which get invalidated after a while
                var successfullyResolvedImages = new List<string>();
                foreach (var menuImageUrl in imageLinks)
                {
                    try
                    {
                        var imageResponse = await client.GetAsync(menuImageUrl);
                        if (imageResponse.IsSuccessStatusCode)
                        {
                            successfullyResolvedImages.Add(menuImageUrl);
                        }
                    }
                    catch (Exception ex)
                    {
                        // TODO Log?
                    }
                }

                System.Threading.Thread.Sleep(TimeSpan.FromSeconds(2));

                // TODO If description starts with "mit" then maybe also add name infront

                dbImages = FoodDBManager.CreateMenuImages(
                    successfullyResolvedImages,
                    searchTerm,
                    language
                );

                if (dbImages.Count > 0)
                    return dbImages.First();
            }

            // Search with description without first line
            // Only first line

            /*
            dbImages = FoodDBManager.GetMenuImages(menu.Name, language);
            if (dbImages.Count > 0)
            {
                return dbImages.First();
            }
            else
            {
                var imageLinks = GetImageFromGoogle(menu.Name, language);
                dbImages = FoodDBManager.CreateMenuImages(imageLinks, menu.Name, language);

                if (dbImages.Count > 0)
                    return dbImages.First();
            }*/



            return null;
        }

        /*private static string GetImageFromGoogle(string text, string lang = "de")
        {
            return Google.ImageSearch(text.Replace("\"", "").Trim(), lang: lang).FirstOrDefault();
        }*/

        public string GetUZHDayOfTheWeek()
        {
            //return "freitag";
            string day = "montag";

            switch (DateTime.Now.DayOfWeek)
            {
                case DayOfWeek.Monday:
                    day = "montag";
                    break;
                case DayOfWeek.Tuesday:
                    day = "dienstag";
                    break;
                case DayOfWeek.Wednesday:
                    day = "mittwoch";
                    break;
                case DayOfWeek.Thursday:
                    day = "donnerstag";
                    break;
                case DayOfWeek.Friday:
                    day = "freitag";
                    break;
                case DayOfWeek.Saturday:
                    day = "samstag";
                    break;
                case DayOfWeek.Sunday:
                    day = "sonntag";
                    break;
            }

            return day;
        }
    }
}
