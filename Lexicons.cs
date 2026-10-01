using System;
using System.Collections.Generic;

namespace FastSwitcher {
internal static class Lexicons {
    // Собранные для проекта небольшие стартовые списки; закрытые словари не используются.
    public const string RussianWords=@"привет пока спасибо пожалуйста да нет как дела хорошо сегодня завтра вчера человек люди слово текст письмо сообщение программа работа друг дом город страна мир время день ночь утро вечер год месяц неделя русский английский язык клавиатура раскладка ошибка исправление пример проверка это тот эта этот мы вы они он она я ты мой твой наш ваш их есть был была будет здесь там когда где почему потому если чтобы очень просто можно нужно снова больше меньше новый старая большой маленький первый последний быстро медленно ручной автоматический компьютер ноутбук окно файл папка документ редактор браузер ссылка адрес почта интернет имя фамилия номер пароль игра терминал исключение правило звук экран кнопка настройка словарь выбор ввод набор фраза предложение письмо вопрос ответ результат добрый доброй день здравствуйте приветствие тест система курсор выделение отмена изменение обратный буква ё ёж ёлка берёза ещё жёлтый чёрный тёплый звёзды шёпот щётка счёт самолёт ребёнок поиск данные таблица проект личный локальный безопасный светлый тёмный тему темы окно окна браузере редакторе слове словами слова набора набором исправить исправлено смешанный смешанная случай случаи доступный доступность начало конец работать работает работают работаем набрать набираю набирает написать пишет пишу выучить запомнить сохранение сохранить открыть закрыть удалить добавить импорт экспорт всё все";
    public const string EnglishWords=@"hello world hi thanks thank you please yes no how are good morning evening night today tomorrow yesterday person people word text message program work friend home city country time day week year month english russian language keyboard layout error correction example check this that these those we they he she i my your our their is was were be here there when where why because if very simple can need again more less new old big small first last fast slow computer window file folder document editor browser link address mail email internet name number password game terminal exception rule sound screen button setting dictionary input phrase sentence question answer result local safe light dark theme cursor selection undo change back letter search data table project personal start end open close delete add import export receive definitely separate weird friend occurred test testing typed typing type write writing words mixed case application focus clipboard formatting protected field another switch switching converted conversion textfield quick the and of to in on with for from at by as an a or not do does did will would should could may have has had it its into out up down over under one two three four five six seven eight nine ten zero app apps user users settings enabled disabled auto automatic manual selection selected select save saved saving load loading import export previous next after before sample common typo capital uppercase lowercase government development information beautiful important different question answer computer keyboard";
    public static readonly Dictionary<string,string> Typos=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase) {
        {"превет","привет"},{"првиет","привет"},{"спосибо","спасибо"},{"пожалуста","пожалуйста"},
        {"здраствуйте","здравствуйте"},{"севодня","сегодня"},{"сичас","сейчас"},{"потму","потому"},
        {"ошипка","ошибка"},{"кагда","когда"},{"вобщем","в общем"},{"извените","извините"},
        {"definately","definitely"},{"recieve","receive"},{"adress","address"},{"seperate","separate"},
        {"teh","the"},{"thier","their"},{"wierd","weird"},{"becuase","because"},
        {"helllo","hello"},{"langauge","language"},{"freind","friend"},{"occured","occurred"}
    };
    public static readonly Dictionary<string,string> Yo=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase) {
        {"елка","ёлка"},{"елки","ёлки"},{"ежик","ёжик"},{"ежики","ёжики"},{"еж","ёж"},
        {"береза","берёза"},{"березы","берёзы"},{"еще","ещё"},{"пчелы","пчёлы"},
        {"желтый","жёлтый"},{"желтая","жёлтая"},{"черный","чёрный"},{"черная","чёрная"},
        {"теплый","тёплый"},{"теплая","тёплая"},{"звезды","звёзды"},{"звездный","звёздный"},
        {"шепот","шёпот"},{"щетка","щётка"},{"счет","счёт"},{"самолет","самолёт"},
        {"ребенок","ребёнок"},{"ребенка","ребёнка"}
    };
}
}
