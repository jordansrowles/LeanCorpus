// Generated from russian.sbl by Snowball 3.2.0 - https://snowballstem.org/

#pragma warning disable 0164
#pragma warning disable 0162

namespace Snowball
{
    using System;
    using System.Text;

    ///<summary>
    ///  This class implements the stemming algorithm defined by a snowball script.
    ///  Generated from russian.sbl by Snowball 3.2.0 - https://snowballstem.org/
    ///</summary>
    ///
    [System.CodeDom.Compiler.GeneratedCode("Snowball", "3.2.0")]
    public partial class RussianStemmer : Stemmer
    {
        private const string g_v = "аеиоуыэюя";

        private static readonly Among[] a_0 = new[]
        {
            new Among("в", -1, 1, 0),
            new Among("ив", 0, 2, 0),
            new Among("ыв", 0, 2, 0),
            new Among("вши", -1, 1, 0),
            new Among("ивши", 3, 2, 0),
            new Among("ывши", 3, 2, 0),
            new Among("вшись", -1, 1, 0),
            new Among("ившись", 6, 2, 0),
            new Among("ывшись", 6, 2, 0)
        };

        private static readonly Among[] a_1 = new[]
        {
            new Among("ее", -1, 1, 0),
            new Among("ие", -1, 1, 0),
            new Among("ое", -1, 1, 0),
            new Among("ые", -1, 1, 0),
            new Among("ими", -1, 1, 0),
            new Among("ыми", -1, 1, 0),
            new Among("ей", -1, 1, 0),
            new Among("ий", -1, 1, 0),
            new Among("ой", -1, 1, 0),
            new Among("ый", -1, 1, 0),
            new Among("ем", -1, 1, 0),
            new Among("им", -1, 1, 0),
            new Among("ом", -1, 1, 0),
            new Among("ым", -1, 1, 0),
            new Among("его", -1, 1, 0),
            new Among("ого", -1, 1, 0),
            new Among("ему", -1, 1, 0),
            new Among("ому", -1, 1, 0),
            new Among("их", -1, 1, 0),
            new Among("ых", -1, 1, 0),
            new Among("ею", -1, 1, 0),
            new Among("ою", -1, 1, 0),
            new Among("ую", -1, 1, 0),
            new Among("юю", -1, 1, 0),
            new Among("ая", -1, 1, 0),
            new Among("яя", -1, 1, 0)
        };

        private static readonly Among[] a_2 = new[]
        {
            new Among("ем", -1, 1, 0),
            new Among("нн", -1, 1, 0),
            new Among("вш", -1, 1, 0),
            new Among("ивш", 2, 2, 0),
            new Among("ывш", 2, 2, 0),
            new Among("щ", -1, 1, 0),
            new Among("ющ", 5, 1, 0),
            new Among("ующ", 6, 2, 0)
        };

        private static readonly Among[] a_3 = new[]
        {
            new Among("сь", -1, 1, 0),
            new Among("ся", -1, 1, 0)
        };

        private static readonly Among[] a_4 = new[]
        {
            new Among("ла", -1, 1, 0),
            new Among("ила", 0, 2, 0),
            new Among("ыла", 0, 2, 0),
            new Among("на", -1, 1, 0),
            new Among("ена", 3, 2, 0),
            new Among("ете", -1, 1, 0),
            new Among("ите", -1, 2, 0),
            new Among("йте", -1, 1, 0),
            new Among("ейте", 7, 2, 0),
            new Among("уйте", 7, 2, 0),
            new Among("ли", -1, 1, 0),
            new Among("или", 10, 2, 0),
            new Among("ыли", 10, 2, 0),
            new Among("й", -1, 1, 0),
            new Among("ей", 13, 2, 0),
            new Among("уй", 13, 2, 0),
            new Among("л", -1, 1, 0),
            new Among("ил", 16, 2, 0),
            new Among("ыл", 16, 2, 0),
            new Among("ем", -1, 1, 0),
            new Among("им", -1, 2, 0),
            new Among("ым", -1, 2, 0),
            new Among("н", -1, 1, 0),
            new Among("ен", 22, 2, 0),
            new Among("ло", -1, 1, 0),
            new Among("ило", 24, 2, 0),
            new Among("ыло", 24, 2, 0),
            new Among("но", -1, 1, 0),
            new Among("ено", 27, 2, 0),
            new Among("нно", 27, 1, 0),
            new Among("ет", -1, 1, 0),
            new Among("ует", 30, 2, 0),
            new Among("ит", -1, 2, 0),
            new Among("ыт", -1, 2, 0),
            new Among("ют", -1, 1, 0),
            new Among("уют", 34, 2, 0),
            new Among("ят", -1, 2, 0),
            new Among("ны", -1, 1, 0),
            new Among("ены", 37, 2, 0),
            new Among("ть", -1, 1, 0),
            new Among("ить", 39, 2, 0),
            new Among("ыть", 39, 2, 0),
            new Among("ешь", -1, 1, 0),
            new Among("ишь", -1, 2, 0),
            new Among("ю", -1, 2, 0),
            new Among("ую", 44, 2, 0)
        };

        private static readonly Among[] a_5 = new[]
        {
            new Among("а", -1, 1, 0),
            new Among("ев", -1, 1, 0),
            new Among("ов", -1, 1, 0),
            new Among("е", -1, 1, 0),
            new Among("ие", 3, 1, 0),
            new Among("ье", 3, 1, 0),
            new Among("и", -1, 1, 0),
            new Among("еи", 6, 1, 0),
            new Among("ии", 6, 1, 0),
            new Among("ами", 6, 1, 0),
            new Among("ями", 6, 1, 0),
            new Among("иями", 10, 1, 0),
            new Among("й", -1, 1, 0),
            new Among("ей", 12, 1, 0),
            new Among("ией", 13, 1, 0),
            new Among("ий", 12, 1, 0),
            new Among("ой", 12, 1, 0),
            new Among("ам", -1, 1, 0),
            new Among("ем", -1, 1, 0),
            new Among("ием", 18, 1, 0),
            new Among("ом", -1, 1, 0),
            new Among("ям", -1, 1, 0),
            new Among("иям", 21, 1, 0),
            new Among("о", -1, 1, 0),
            new Among("у", -1, 1, 0),
            new Among("ах", -1, 1, 0),
            new Among("ях", -1, 1, 0),
            new Among("иях", 26, 1, 0),
            new Among("ы", -1, 1, 0),
            new Among("ь", -1, 1, 0),
            new Among("ю", -1, 1, 0),
            new Among("ию", 30, 1, 0),
            new Among("ью", 30, 1, 0),
            new Among("я", -1, 1, 0),
            new Among("ия", 33, 1, 0),
            new Among("ья", 33, 1, 0)
        };

        private static readonly Among[] a_6 = new[]
        {
            new Among("ост", -1, 1, 0),
            new Among("ость", -1, 1, 0)
        };

        private static readonly Among[] a_7 = new[]
        {
            new Among("ейше", -1, 1, 0),
            new Among("н", -1, 2, 0),
            new Among("ейш", -1, 1, 0),
            new Among("ь", -1, 3, 0)
        };


        protected override bool stem()
        {
            int among_var;
            int I_p2;
            int I_pV;
            {
                int c1 = cursor;
                while (true)
                {
                    int c2 = cursor;
                    while (true)
                    {
                        int c3 = cursor;
                        bra = cursor;
                        if (!(eq_s("ё")))
                        {
                            goto lab2;
                        }
                        ket = cursor;
                        cursor = c3;
                        break;
                    lab2: ;
                        cursor = c3;
                        if (cursor >= limit)
                        {
                            goto lab1;
                        }
                        cursor++;
                    }
                    slice_from("е");
                    continue;
                lab1: ;
                    cursor = c2;
                    break;
                }
                cursor = c1;
            }
            I_pV = limit;
            I_p2 = limit;
            {
                int c4 = cursor;
                {

                    int ret = out_grouping(g_v, 1072, 1103, true);
                    if (ret < 0)
                    {
                        goto lab4;
                    }

                    cursor += ret;
                }
                I_pV = cursor;
                {

                    int ret = in_grouping(g_v, 1072, 1103, true);
                    if (ret < 0)
                    {
                        goto lab4;
                    }

                    cursor += ret;
                }
                {

                    int ret = out_grouping(g_v, 1072, 1103, true);
                    if (ret < 0)
                    {
                        goto lab4;
                    }

                    cursor += ret;
                }
                {

                    int ret = in_grouping(g_v, 1072, 1103, true);
                    if (ret < 0)
                    {
                        goto lab4;
                    }

                    cursor += ret;
                }
                I_p2 = cursor;
            lab4: ;
                cursor = c4;
            }
            limit_backward = cursor;
            cursor = limit;
            if (cursor < I_pV)
            {
                return false;
            }
            int c5 = limit_backward;
            limit_backward = I_pV;
            {
                int c6 = limit - cursor;
                {
                    int c7 = limit - cursor;
                    ket = cursor;
                    among_var = find_among_b(a_0, null);
                    if (among_var == 0)
                    {
                        goto lab7;
                    }
                    bra = cursor;
                    switch (among_var) {
                        case 1: {
                            if (!(eq_s_b("а")))
                            {
                                goto lab9;
                            }
                            goto lab8;
                        lab9: ;
                            if (!(eq_s_b("я")))
                            {
                                goto lab7;
                            }
                        lab8: ;
                            slice_del();
                            break;
                        }
                        case 2: {
                            slice_del();
                            break;
                        }
                    }
                    goto lab6;
                lab7: ;
                    cursor = limit - c7;
                    {
                        int c8 = limit - cursor;
                        ket = cursor;
                        if (find_among_b(a_3, null) == 0)
                        {
                            {
                                cursor = limit - c8;
                                goto lab10;
                            }
                        }
                        bra = cursor;
                        slice_del();
                    lab10: ;
                    }
                    {
                        int c9 = limit - cursor;
                        ket = cursor;
                        if (find_among_b(a_1, null) == 0)
                        {
                            goto lab12;
                        }
                        bra = cursor;
                        slice_del();
                        {
                            int c10 = limit - cursor;
                            ket = cursor;
                            among_var = find_among_b(a_2, null);
                            if (among_var == 0)
                            {
                                {
                                    cursor = limit - c10;
                                    goto lab13;
                                }
                            }
                            bra = cursor;
                            switch (among_var) {
                                case 1: {
                                    if (!(eq_s_b("а")))
                                    {
                                        goto lab15;
                                    }
                                    goto lab14;
                                lab15: ;
                                    if (!(eq_s_b("я")))
                                    {
                                        {
                                            cursor = limit - c10;
                                            goto lab13;
                                        }
                                    }
                                lab14: ;
                                    slice_del();
                                    break;
                                }
                                case 2: {
                                    slice_del();
                                    break;
                                }
                            }
                        lab13: ;
                        }
                        goto lab11;
                    lab12: ;
                        cursor = limit - c9;
                        ket = cursor;
                        among_var = find_among_b(a_4, null);
                        if (among_var == 0)
                        {
                            goto lab16;
                        }
                        bra = cursor;
                        switch (among_var) {
                            case 1: {
                                if (!(eq_s_b("а")))
                                {
                                    goto lab18;
                                }
                                goto lab17;
                            lab18: ;
                                if (!(eq_s_b("я")))
                                {
                                    goto lab16;
                                }
                            lab17: ;
                                slice_del();
                                break;
                            }
                            case 2: {
                                slice_del();
                                break;
                            }
                        }
                        goto lab11;
                    lab16: ;
                        cursor = limit - c9;
                        ket = cursor;
                        if (find_among_b(a_5, null) == 0)
                        {
                            goto lab5;
                        }
                        bra = cursor;
                        slice_del();
                    }
                lab11: ;
                }
            lab6: ;
            lab5: ;
                cursor = limit - c6;
            }
            {
                int c11 = limit - cursor;
                ket = cursor;
                if (!(eq_s_b("и")))
                {
                    {
                        cursor = limit - c11;
                        goto lab19;
                    }
                }
                bra = cursor;
                slice_del();
            lab19: ;
            }
            {
                int c12 = limit - cursor;
                ket = cursor;
                if (find_among_b(a_6, null) == 0)
                {
                    goto lab20;
                }
                bra = cursor;
                if (I_p2 > cursor)
                {
                    goto lab20;
                }
                slice_del();
            lab20: ;
                cursor = limit - c12;
            }
            {
                int c13 = limit - cursor;
                ket = cursor;
                among_var = find_among_b(a_7, null);
                if (among_var == 0)
                {
                    goto lab21;
                }
                bra = cursor;
                switch (among_var) {
                    case 1: {
                        slice_del();
                        ket = cursor;
                        if (!(eq_s_b("н")))
                        {
                            goto lab21;
                        }
                        bra = cursor;
                        if (!(eq_s_b("н")))
                        {
                            goto lab21;
                        }
                        slice_del();
                        break;
                    }
                    case 2: {
                        if (!(eq_s_b("н")))
                        {
                            goto lab21;
                        }
                        slice_del();
                        break;
                    }
                    case 3: {
                        slice_del();
                        break;
                    }
                }
            lab21: ;
                cursor = limit - c13;
            }
            limit_backward = c5;
            cursor = limit_backward;
            return true;
        }

    }
}

