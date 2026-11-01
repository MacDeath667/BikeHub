using BikeHub.Application.Community;

namespace BikeHub.Api.Services;

// Shared singleton storage: mutations survive requests, but deliberately reset on restart.
public sealed class MockCommunityStore
{
    private const string UserHandlePrefix = "rider-";

    internal object Gate { get; } = new();
    private static readonly Guid SeedUserId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    internal List<UserDto> Users { get; } = [];
    internal List<BikeDto> Bikes { get; } = [];
    internal List<PostDto> Posts { get; } = [];
    internal Dictionary<Guid, List<CommentDto>> Comments { get; } = [];
    internal List<CommunityDto> Communities { get; } = [];
    internal HashSet<(Guid UserId, Guid TargetId)> Likes { get; } = [];
    internal HashSet<(Guid UserId, Guid TargetId)> Bookmarks { get; } = [];
    internal HashSet<(Guid UserId, Guid TargetId)> Following { get; } = [];
    internal HashSet<(Guid UserId, Guid TargetId)> Memberships { get; } = [];

    public MockCommunityStore()
    {
        Users.AddRange([
            new(SeedUserId, "Александр Волков", "alexrides", "Москва", "АВ", Constants.MockData.DefaultUserColor,
                "Коллекционирую километры и хорошие истории. Гравий, кофе и дорога за горизонт.", 128, false, 0, 0),
            new(Guid.Parse("10000000-0000-0000-0000-000000000002"), "Михаил Соколов", "misha.on.wheels", "Санкт-Петербург", "МС", "sand",
                "Выбираю дорогу подлиннее. Езжу на шоссе, собираю велосипеды и рассказываю, что из этого выходит.", 846, true, 0, 0),
            new(Guid.Parse("10000000-0000-0000-0000-000000000003"), "Анна Белова", "annabike", "Москва", "АБ", "rose",
                "В лес при любой погоде. Люблю трейлы, простые решения и велосипеды с характером.", 612, false, 0, 0),
            new(Guid.Parse("10000000-0000-0000-0000-000000000004"), "Денис Орлов", "denis.gravel", "Казань", "ДО", "blue",
                "Гравийные маршруты, велопоходы и маленькие открытия по пути.", 423, true, 0, 0),
            new(Guid.Parse("10000000-0000-0000-0000-000000000005"), "Илья Кузнецов", "ilya.fixed", "Екатеринбург", "ИК", "lavender",
                "Город — мой любимый маршрут. Собираю сталь и разбираюсь в деталях.", 285, false, 0, 0)
        ]);

        Bikes.AddRange([
            new(Constants.MockData.FeaturedBikeId, Users[0].Id, "Canyon Grizl CF SL 7", "Мой билет туда, где заканчивается асфальт. Карбоновая рама, GRX и никаких лишних планов.", "Canyon", BikeCategory.Gravel, 2024, 2840, Constants.Images.Gravel, "Матовый зелёный", Users[0]),
            new(Guid.Parse("20000000-0000-0000-0000-000000000002"), Users[1].Id, "Specialized Allez Sprint", "Быстрый алюминий для длинных выходных. Shimano 105, колёса DT Swiss и немного любви к деталям.", "Specialized", BikeCategory.Road, 2023, 8450, Constants.Images.Road, "Серебристый", Users[1]),
            new(Guid.Parse("20000000-0000-0000-0000-000000000003"), Users[2].Id, "Trek Fuel EX 8", "Трейловый друг, который прощает чуть больше, чем нужно. 140 мм хорошего настроения.", "Trek", BikeCategory.Mtb, 2024, 1620, Constants.Images.Trail, "Оливковый", Users[2]),
            new(Guid.Parse("20000000-0000-0000-0000-000000000004"), Users[3].Id, "Merida Silex 400", "Универсальный велосипед для поездок после работы и больших приключений с сумками.", "Merida", BikeCategory.Gravel, 2022, 12300, Constants.Images.Adventure, "Песочный", Users[3]),
            new(Guid.Parse("20000000-0000-0000-0000-000000000005"), Users[4].Id, "Fuji Feather", "Простая стальная классика. Одна передача и целый город возможностей.", "Fuji", BikeCategory.City, 2021, 5600, Constants.Images.City, "Белый", Users[4]),
            new(Guid.Parse("20000000-0000-0000-0000-000000000006"), Users[0].Id, "Merida Scultura 400", "Первый шоссейник и по-прежнему любимый. На нём открыл для себя утренние сотки.", "Merida", BikeCategory.Road, 2022, 6470, Constants.Images.Road, "Графит", Users[0])
        ]);

        var now = DateTimeOffset.UtcNow;
        Posts.AddRange([
            new(Guid.Parse("30000000-0000-0000-0000-000000000001"), Users[1], Bikes[1],
                "Туда, где нет связи. Мои первые 200 км за день",
                "Иногда лучший план на выходные — просто выехать пораньше. В этот раз решили уйти от знакомых маршрутов и доехать до Ладоги маленькими дорогами.\n\nСтартовали в шесть утра: пустой город, холодный воздух и первый кофе на заправке. После семидесятого километра асфальт стал хуже, зато виды — лучше. За весь день почти не доставали телефоны.\n\nПо питанию: два батончика, банан и остановка на полноценный обед. Воды ушло около четырёх литров. На последних тридцати километрах очень пригодилась привычка есть до того, как проголодаешься.\n\nИтог — 204 км, 8 часов в седле и желание повторить. Allez не подвёл ни разу. А вы помните свою первую двухсотку?", "ride", Constants.Images.Adventure, ["велопутешествия", "шоссе", "200км"], now.AddHours(-2), 128, 3, false, false),
            new(Guid.Parse("30000000-0000-0000-0000-000000000002"), Users[2], Bikes[2],
                "Большое ТО перед сезоном: что действительно стоило заменить",
                "Разобрала велосипед после тысячи километров по лесам и грязи. Делюсь списком работ и тем, на чём удалось сэкономить без потери качества.\n\nНачала с трансмиссии: цепь растянулась до 0,75, но кассета ещё вполне жива. Поставила новую цепь, вымыла ролики переключателя и сменила трос. Переключение снова как в первый день.\n\nВ вилке обновила масло и пыльники. Заодно прокачала тормоза и проверила колодки — передние заменю через пару поездок. Все подшипники подвески пока в порядке.\n\nГлавный вывод: регулярная мойка и смазка экономят больше, чем скидки на запчасти. Полный список расходников оставила в комментариях.", "maintenance", Constants.Images.Trail, ["обслуживание", "mtb", "своимируками"], now.AddHours(-4), 76, 1, false, true),
            new(Guid.Parse("30000000-0000-0000-0000-000000000003"), Users[3], Bikes[3],
                "Silex спустя 12 000 км. Велосипед, который меняет планы",
                "Покупал его для дороги на работу. Теперь по дороге на работу регулярно оказываюсь в соседнем районе, в лесу или у реки.\n\nЗа три сезона поменял покрышки на 42 мм, поставил компактный руль и нашёл идеальные сумки. Всё остальное осталось штатным. Алюминиевая рама пережила и поезд, и пару падений, и мои эксперименты с багажником.\n\nЛучшее обновление — не деталь, а привычка держать велосипед готовым к выезду. Десять минут на сборы, и вот ты уже едешь навстречу новому маршруту.", "story", Constants.Images.Gravel, ["гравий", "обзор", "Merida"], now.AddHours(-7), 94, 0, true, false),
            new(Guid.Parse("30000000-0000-0000-0000-000000000004"), Users[4], Bikes[4],
                "Меньше деталей — больше города. Собрал свой идеальный синглспид",
                "Закончил сборку, которую откладывал всю зиму. Оставил стальную раму, заменил контактные точки и собрал новые колёса.\n\nРуль шириной 620 мм, удобное седло и передаточное отношение 46/17 — для моих городских маршрутов оказалось в самый раз. Никаких рекордов, просто удовольствие от каждой поездки.\n\nСледующий шаг — найти аккуратные крылья, чтобы не зависеть от прогноза погоды.", "upgrade", Constants.Images.City, ["кастом", "город", "сталь"], now.AddHours(-12), 53, 0, false, false),
            new(Guid.Parse("30000000-0000-0000-0000-000000000005"), Users[0], Bikes[0],
                "Знакомство с Grizl: первый выезд по осеннему гравию",
                "Наконец-то выбрался за город на новом велосипеде. Петля на 65 км: немного асфальта, много гравия и один участок, где пришлось идти пешком.\n\nПосадка оказалась комфортнее, чем ожидал, а широкие покрышки добавили уверенности на мокрых листьях. Давление 2,2 бара — кажется, попал с первого раза.\n\nЗавёл бортжурнал, чтобы не терять впечатления и историю обслуживания. Впереди ещё много дорог!", "ride", Constants.Images.Gravel, ["гравий", "Canyon", "первыйвыезд"], now.AddDays(-1), 31, 0, false, false)
        ]);

        Comments[Posts[0].Id] = [
            new(Guid.NewGuid(), Users[3], "Классный маршрут! До Ладоги тоже давно хочу добраться. Какой участок понравился больше всего?", now.AddMinutes(-90)),
            new(Guid.NewGuid(), Users[1], "Дорога после Коккорево — тихая, почти без машин. И вид на озеро в конце стоит каждого километра.", now.AddMinutes(-65)),
            new(Guid.NewGuid(), Users[2], "Поздравляю с первой двухсоткой! После таких поездок обычная сотка уже кажется разминкой :) ", now.AddMinutes(-30))
        ];
        Comments[Posts[1].Id] = [new(Guid.NewGuid(), Users[0], "Спасибо за опыт! Тоже начну с проверки цепи и колодок.", now.AddHours(-2))];

        Communities.AddRange([
            new(Guid.Parse("40000000-0000-0000-0000-000000000001"), "Гравий без границ", "Маршруты вне асфальта, байкпакинг и люди, которым всегда интересно, что за поворотом.", BikeCategory.Gravel, Constants.Images.Gravel, 1248, true),
            new(Guid.Parse("40000000-0000-0000-0000-000000000002"), "Шоссейный клуб", "Утренние выезды, длинные дистанции и всё, что помогает ехать чуть дальше.", BikeCategory.Road, Constants.Images.Road, 2350, false),
            new(Guid.Parse("40000000-0000-0000-0000-000000000003"), "Лес зовёт", "Трейлы, подвески, грязь и хорошие компании. Встречаемся там, где кончается дорога.", BikeCategory.Mtb, Constants.Images.Trail, 964, false),
            new(Guid.Parse("40000000-0000-0000-0000-000000000004"), "Город на двух колёсах", "На работу и за кофе. Обсуждаем городские маршруты, сборки и велокультуру.", BikeCategory.City, Constants.Images.City, 1786, false)
        ]);
    }

    public UserDto EnsureUser(Guid id, string name)
    {
        lock (Gate)
        {
            var user = Users.FirstOrDefault(x => x.Id == id);
            if (user is null)
            {
                var initials = string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Take(2).Select(part => part[..1].ToUpperInvariant()));
                user = new(id, name, UserHandlePrefix + id.ToString("N")[^16..], string.Empty, initials, Constants.MockData.DefaultUserColor, string.Empty, 0, false, 0, 0);
                Users.Add(user);
            }
            return Hydrate(user, id);
        }
    }

    internal UserDto Hydrate(UserDto user, Guid? viewer)
    {
        var canonical = Users.First(x => x.Id == user.Id);
        return canonical with
        {
            IsFollowing = viewer.HasValue && Following.Contains((viewer.Value, user.Id)),
            Followers = canonical.Followers + Following.Count(x => x.TargetId == user.Id),
            BikesCount = Bikes.Count(x => x.OwnerId == user.Id),
            PostsCount = Posts.Count(x => x.Author.Id == user.Id)
        };
    }
    internal BikeDto Hydrate(BikeDto bike, Guid? viewer) => bike with { Owner = Hydrate(bike.Owner, viewer) };
    internal CommunityDto Hydrate(CommunityDto community, Guid? viewer) => community with
    {
        IsJoined = viewer.HasValue && Memberships.Contains((viewer.Value, community.Id)),
        Members = community.Members + Memberships.Count(x => x.TargetId == community.Id)
    };
    internal PostDto Hydrate(PostDto post, Guid? viewer) => post with
    {
        Author = Hydrate(post.Author, viewer),
        Bike = Hydrate(post.Bike, viewer),
        IsLiked = viewer.HasValue && Likes.Contains((viewer.Value, post.Id)),
        IsSaved = viewer.HasValue && Bookmarks.Contains((viewer.Value, post.Id)),
        Likes = post.Likes + Likes.Count(x => x.TargetId == post.Id),
        CommentsCount = Comments.GetValueOrDefault(post.Id)?.Count ?? 0
    };
    internal static Page<T> Paginate<T>(IEnumerable<T> source, int page, int size)
    {
        var items = source.ToList();
        var offset = (long)(page - 1) * size;
        return new(offset >= items.Count ? [] : items.Skip((int)offset).Take(size).ToList(), items.Count, page, size);
    }
}
