// Generated from french.sbl by Snowball 3.2.0 - https://snowballstem.org/

#pragma warning disable 0164
#pragma warning disable 0162

namespace Snowball
{
    using System;
    using System.Text;

    ///<summary>
    ///  This class implements the stemming algorithm defined by a snowball script.
    ///  Generated from french.sbl by Snowball 3.2.0 - https://snowballstem.org/
    ///</summary>
    ///
    [System.CodeDom.Compiler.GeneratedCode("Snowball", "3.2.0")]
    public partial class FrenchStemmer : Stemmer
    {
        private const string g_v = "aeiouyàâèéêëîïôùû";
        private const string g_oux_ending = "bhjlnp";
        private const string g_elision_char = "cdjlmnstz";
        private const string g_keep_with_s = "aiosuè";

        private static readonly Among[] a_0 = new[]
        {
            new Among("col", -1, -1, 0),
            new Among("ni", -1, 1, 0),
            new Among("par", -1, -1, 0),
            new Among("tap", -1, -1, 0)
        };

        private static readonly Among[] a_1 = new[]
        {
            new Among("", -1, 7, 0),
            new Among("H", 0, 6, 0),
            new Among("He", 1, 4, 0),
            new Among("Hi", 1, 5, 0),
            new Among("I", 0, 1, 0),
            new Among("U", 0, 2, 0),
            new Among("Y", 0, 3, 0)
        };

        private static readonly Among[] a_2 = new[]
        {
            new Among("iqU", -1, 3, 0),
            new Among("abl", -1, 3, 0),
            new Among("Ièr", -1, 4, 0),
            new Among("ièr", -1, 4, 0),
            new Among("eus", -1, 2, 0),
            new Among("iv", -1, 1, 0)
        };

        private static readonly Among[] a_3 = new[]
        {
            new Among("ic", -1, 2, 0),
            new Among("abil", -1, 1, 0),
            new Among("iv", -1, 3, 0)
        };

        private static readonly Among[] a_4 = new[]
        {
            new Among("iqUe", -1, 1, 0),
            new Among("atrice", -1, 2, 0),
            new Among("ance", -1, 1, 0),
            new Among("ence", -1, 5, 0),
            new Among("logie", -1, 3, 0),
            new Among("able", -1, 1, 0),
            new Among("isme", -1, 1, 0),
            new Among("euse", -1, 12, 0),
            new Among("iste", -1, 1, 0),
            new Among("ive", -1, 8, 0),
            new Among("if", -1, 8, 0),
            new Among("usion", -1, 4, 0),
            new Among("ation", -1, 2, 0),
            new Among("ution", -1, 4, 0),
            new Among("ateur", -1, 2, 0),
            new Among("iqUes", -1, 1, 0),
            new Among("atrices", -1, 2, 0),
            new Among("ances", -1, 1, 0),
            new Among("ences", -1, 5, 0),
            new Among("logies", -1, 3, 0),
            new Among("ables", -1, 1, 0),
            new Among("ismes", -1, 1, 0),
            new Among("euses", -1, 12, 0),
            new Among("istes", -1, 1, 0),
            new Among("ives", -1, 8, 0),
            new Among("ifs", -1, 8, 0),
            new Among("usions", -1, 4, 0),
            new Among("ations", -1, 2, 0),
            new Among("utions", -1, 4, 0),
            new Among("ateurs", -1, 2, 0),
            new Among("ments", -1, 16, 0),
            new Among("ements", 30, 6, 0),
            new Among("issements", 31, 13, 0),
            new Among("ités", -1, 7, 0),
            new Among("ment", -1, 16, 0),
            new Among("ement", 34, 6, 0),
            new Among("issement", 35, 13, 0),
            new Among("amment", 34, 14, 0),
            new Among("emment", 34, 15, 0),
            new Among("aux", -1, 10, 0),
            new Among("eaux", 39, 9, 0),
            new Among("eux", -1, 1, 0),
            new Among("oux", -1, 11, 0),
            new Among("ité", -1, 7, 0)
        };

        private static readonly Among[] a_5 = new[]
        {
            new Among("ira", -1, 1, 0),
            new Among("ie", -1, 1, 0),
            new Among("isse", -1, 1, 0),
            new Among("issante", -1, 1, 0),
            new Among("i", -1, 1, 0),
            new Among("irai", 4, 1, 0),
            new Among("ir", -1, 1, 0),
            new Among("iras", -1, 1, 0),
            new Among("ies", -1, 1, 0),
            new Among("îmes", -1, 1, 0),
            new Among("isses", -1, 1, 0),
            new Among("issantes", -1, 1, 0),
            new Among("îtes", -1, 1, 0),
            new Among("is", -1, 1, 0),
            new Among("irais", 13, 1, 0),
            new Among("issais", 13, 1, 0),
            new Among("irions", -1, 1, 0),
            new Among("issions", -1, 1, 0),
            new Among("irons", -1, 1, 0),
            new Among("issons", -1, 1, 0),
            new Among("issants", -1, 1, 0),
            new Among("it", -1, 1, 0),
            new Among("irait", 21, 1, 0),
            new Among("issait", 21, 1, 0),
            new Among("issant", -1, 1, 0),
            new Among("iraIent", -1, 1, 0),
            new Among("issaIent", -1, 1, 0),
            new Among("irent", -1, 1, 0),
            new Among("issent", -1, 1, 0),
            new Among("iront", -1, 1, 0),
            new Among("ît", -1, 1, 0),
            new Among("iriez", -1, 1, 0),
            new Among("issiez", -1, 1, 0),
            new Among("irez", -1, 1, 0),
            new Among("issez", -1, 1, 0)
        };

        private static readonly Among[] a_6 = new[]
        {
            new Among("al", -1, 1, 0),
            new Among("épl", -1, -1, 0),
            new Among("auv", -1, -1, 0)
        };

        private static readonly Among[] a_7 = new[]
        {
            new Among("a", -1, 3, 0),
            new Among("era", 0, 2, 0),
            new Among("aise", -1, 4, 0),
            new Among("asse", -1, 3, 0),
            new Among("ante", -1, 3, 0),
            new Among("ée", -1, 2, 0),
            new Among("ai", -1, 3, 0),
            new Among("erai", 6, 2, 0),
            new Among("er", -1, 2, 0),
            new Among("as", -1, 3, 0),
            new Among("eras", 9, 2, 0),
            new Among("âmes", -1, 3, 0),
            new Among("aises", -1, 4, 0),
            new Among("asses", -1, 3, 0),
            new Among("antes", -1, 3, 0),
            new Among("âtes", -1, 3, 0),
            new Among("ées", -1, 2, 0),
            new Among("ais", -1, 4, 0),
            new Among("eais", 17, 2, 0),
            new Among("erais", 17, 2, 0),
            new Among("ions", -1, 1, 0),
            new Among("erions", 20, 2, 0),
            new Among("assions", 20, 3, 0),
            new Among("erons", -1, 2, 0),
            new Among("ants", -1, 3, 0),
            new Among("és", -1, 2, 0),
            new Among("ait", -1, 3, 0),
            new Among("erait", 26, 2, 0),
            new Among("ant", -1, 3, 0),
            new Among("aIent", -1, 3, 0),
            new Among("eraIent", 29, 2, 0),
            new Among("èrent", -1, 2, 0),
            new Among("assent", -1, 3, 0),
            new Among("eront", -1, 2, 0),
            new Among("ât", -1, 3, 0),
            new Among("ez", -1, 2, 0),
            new Among("iez", 35, 2, 0),
            new Among("eriez", 36, 2, 0),
            new Among("assiez", 36, 3, 0),
            new Among("erez", 35, 2, 0),
            new Among("é", -1, 2, 0)
        };

        private static readonly Among[] a_8 = new[]
        {
            new Among("e", -1, 3, 0),
            new Among("Ière", 0, 2, 0),
            new Among("ière", 0, 2, 0),
            new Among("ion", -1, 1, 0),
            new Among("Ier", -1, 2, 0),
            new Among("ier", -1, 2, 0)
        };

        private static readonly Among[] a_9 = new[]
        {
            new Among("ell", -1, -1, 0),
            new Among("eill", -1, -1, 0),
            new Among("enn", -1, -1, 0),
            new Among("onn", -1, -1, 0),
            new Among("ett", -1, -1, 0)
        };


        protected override bool stem()
        {
            int among_var;
            int I_p2;
            int I_p1;
            int I_pV;
            {
                int c1 = cursor;
                bra = cursor;
                if (in_grouping(g_elision_char, 99, 122, false) != 0)
                {
                    goto lab2;
                }
                goto lab1;
            lab2: ;
                if (!(eq_s("qu")))
                {
                    goto lab0;
                }
            lab1: ;
                if (!(eq_s("'")))
                {
                    goto lab0;
                }
                ket = cursor;
                if (cursor >= limit)
                {
                    goto lab0;
                }
                slice_del();
            lab0: ;
                cursor = c1;
            }
            {
                int c2 = cursor;
                while (true)
                {
                    int c3 = cursor;
                    while (true)
                    {
                        int c4 = cursor;
                        {
                            int c5 = cursor;
                            if (in_grouping(g_v, 97, 251, false) != 0)
                            {
                                goto lab7;
                            }
                            bra = cursor;
                            {
                                int c6 = cursor;
                                if (!(eq_s("u")))
                                {
                                    goto lab9;
                                }
                                ket = cursor;
                                if (in_grouping(g_v, 97, 251, false) != 0)
                                {
                                    goto lab9;
                                }
                                slice_from("U");
                                goto lab8;
                            lab9: ;
                                cursor = c6;
                                if (!(eq_s("i")))
                                {
                                    goto lab10;
                                }
                                ket = cursor;
                                if (in_grouping(g_v, 97, 251, false) != 0)
                                {
                                    goto lab10;
                                }
                                slice_from("I");
                                goto lab8;
                            lab10: ;
                                cursor = c6;
                                if (!(eq_s("y")))
                                {
                                    goto lab7;
                                }
                                ket = cursor;
                                slice_from("Y");
                            }
                        lab8: ;
                            goto lab6;
                        lab7: ;
                            cursor = c5;
                            bra = cursor;
                            if (!(eq_s("ë")))
                            {
                                goto lab11;
                            }
                            ket = cursor;
                            slice_from("He");
                            goto lab6;
                        lab11: ;
                            cursor = c5;
                            bra = cursor;
                            if (!(eq_s("ï")))
                            {
                                goto lab12;
                            }
                            ket = cursor;
                            slice_from("Hi");
                            goto lab6;
                        lab12: ;
                            cursor = c5;
                            bra = cursor;
                            if (!(eq_s("y")))
                            {
                                goto lab13;
                            }
                            ket = cursor;
                            if (in_grouping(g_v, 97, 251, false) != 0)
                            {
                                goto lab13;
                            }
                            slice_from("Y");
                            goto lab6;
                        lab13: ;
                            cursor = c5;
                            if (!(eq_s("q")))
                            {
                                goto lab5;
                            }
                            bra = cursor;
                            if (!(eq_s("u")))
                            {
                                goto lab5;
                            }
                            ket = cursor;
                            slice_from("U");
                        }
                    lab6: ;
                        cursor = c4;
                        break;
                    lab5: ;
                        cursor = c4;
                        if (cursor >= limit)
                        {
                            goto lab4;
                        }
                        cursor++;
                    }
                    continue;
                lab4: ;
                    cursor = c3;
                    break;
                }
                cursor = c2;
            }
            I_pV = limit;
            I_p1 = limit;
            I_p2 = limit;
            {
                int c7 = cursor;
                {
                    int c8 = cursor;
                    if (in_grouping(g_v, 97, 251, false) != 0)
                    {
                        goto lab17;
                    }
                    if (in_grouping(g_v, 97, 251, false) != 0)
                    {
                        goto lab17;
                    }
                    if (cursor >= limit)
                    {
                        goto lab17;
                    }
                    cursor++;
                    goto lab16;
                lab17: ;
                    cursor = c8;
                    among_var = find_among(a_0, null);
                    if (among_var == 0)
                    {
                        goto lab18;
                    }
                    switch (among_var) {
                        case 1: {
                            if (in_grouping(g_v, 97, 251, false) != 0)
                            {
                                goto lab18;
                            }
                            break;
                        }
                    }
                    goto lab16;
                lab18: ;
                    cursor = c8;
                    if (cursor >= limit)
                    {
                        goto lab15;
                    }
                    cursor++;
                    {

                        int ret = out_grouping(g_v, 97, 251, true);
                        if (ret < 0)
                        {
                            goto lab15;
                        }

                        cursor += ret;
                    }
                }
            lab16: ;
                I_pV = cursor;
            lab15: ;
                cursor = c7;
            }
            {
                int c9 = cursor;
                {

                    int ret = out_grouping(g_v, 97, 251, true);
                    if (ret < 0)
                    {
                        goto lab19;
                    }

                    cursor += ret;
                }
                {

                    int ret = in_grouping(g_v, 97, 251, true);
                    if (ret < 0)
                    {
                        goto lab19;
                    }

                    cursor += ret;
                }
                I_p1 = cursor;
                {

                    int ret = out_grouping(g_v, 97, 251, true);
                    if (ret < 0)
                    {
                        goto lab19;
                    }

                    cursor += ret;
                }
                {

                    int ret = in_grouping(g_v, 97, 251, true);
                    if (ret < 0)
                    {
                        goto lab19;
                    }

                    cursor += ret;
                }
                I_p2 = cursor;
            lab19: ;
                cursor = c9;
            }
            limit_backward = cursor;
            cursor = limit;
            {
                int c10 = limit - cursor;
                {
                    int c11 = limit - cursor;
                    int c12 = limit - cursor;
                    {
                        int c13 = limit - cursor;
                        ket = cursor;
                        among_var = find_among_b(a_4, null);
                        if (among_var == 0)
                        {
                            goto lab24;
                        }
                        bra = cursor;
                        switch (among_var) {
                            case 1: {
                                if (I_p2 > cursor)
                                {
                                    goto lab24;
                                }
                                slice_del();
                                break;
                            }
                            case 2: {
                                if (I_p2 > cursor)
                                {
                                    goto lab24;
                                }
                                slice_del();
                                {
                                    int c14 = limit - cursor;
                                    ket = cursor;
                                    if (!(eq_s_b("ic")))
                                    {
                                        {
                                            cursor = limit - c14;
                                            goto lab25;
                                        }
                                    }
                                    bra = cursor;
                                    {
                                        int c15 = limit - cursor;
                                        if (I_p2 > cursor)
                                        {
                                            goto lab27;
                                        }
                                        slice_del();
                                        goto lab26;
                                    lab27: ;
                                        cursor = limit - c15;
                                        slice_from("iqU");
                                    }
                                lab26: ;
                                lab25: ;
                                }
                                break;
                            }
                            case 3: {
                                if (I_p2 > cursor)
                                {
                                    goto lab24;
                                }
                                slice_from("log");
                                break;
                            }
                            case 4: {
                                if (I_p2 > cursor)
                                {
                                    goto lab24;
                                }
                                slice_from("u");
                                break;
                            }
                            case 5: {
                                if (I_p2 > cursor)
                                {
                                    goto lab24;
                                }
                                slice_from("ent");
                                break;
                            }
                            case 6: {
                                if (I_pV > cursor)
                                {
                                    goto lab24;
                                }
                                slice_del();
                                {
                                    int c16 = limit - cursor;
                                    ket = cursor;
                                    among_var = find_among_b(a_2, null);
                                    if (among_var == 0)
                                    {
                                        {
                                            cursor = limit - c16;
                                            goto lab28;
                                        }
                                    }
                                    bra = cursor;
                                    switch (among_var) {
                                        case 1: {
                                            if (I_p2 > cursor)
                                            {
                                                {
                                                    cursor = limit - c16;
                                                    goto lab28;
                                                }
                                            }
                                            slice_del();
                                            ket = cursor;
                                            if (!(eq_s_b("at")))
                                            {
                                                {
                                                    cursor = limit - c16;
                                                    goto lab28;
                                                }
                                            }
                                            bra = cursor;
                                            if (I_p2 > cursor)
                                            {
                                                {
                                                    cursor = limit - c16;
                                                    goto lab28;
                                                }
                                            }
                                            slice_del();
                                            break;
                                        }
                                        case 2: {
                                            {
                                                int c17 = limit - cursor;
                                                if (I_p2 > cursor)
                                                {
                                                    goto lab30;
                                                }
                                                slice_del();
                                                goto lab29;
                                            lab30: ;
                                                cursor = limit - c17;
                                                if (I_p1 > cursor)
                                                {
                                                    {
                                                        cursor = limit - c16;
                                                        goto lab28;
                                                    }
                                                }
                                                slice_from("eux");
                                            }
                                        lab29: ;
                                            break;
                                        }
                                        case 3: {
                                            if (I_p2 > cursor)
                                            {
                                                {
                                                    cursor = limit - c16;
                                                    goto lab28;
                                                }
                                            }
                                            slice_del();
                                            break;
                                        }
                                        case 4: {
                                            if (I_pV > cursor)
                                            {
                                                {
                                                    cursor = limit - c16;
                                                    goto lab28;
                                                }
                                            }
                                            slice_from("i");
                                            break;
                                        }
                                    }
                                lab28: ;
                                }
                                break;
                            }
                            case 7: {
                                if (I_p2 > cursor)
                                {
                                    goto lab24;
                                }
                                slice_del();
                                {
                                    int c18 = limit - cursor;
                                    ket = cursor;
                                    among_var = find_among_b(a_3, null);
                                    if (among_var == 0)
                                    {
                                        {
                                            cursor = limit - c18;
                                            goto lab31;
                                        }
                                    }
                                    bra = cursor;
                                    switch (among_var) {
                                        case 1: {
                                            {
                                                int c19 = limit - cursor;
                                                if (I_p2 > cursor)
                                                {
                                                    goto lab33;
                                                }
                                                slice_del();
                                                goto lab32;
                                            lab33: ;
                                                cursor = limit - c19;
                                                slice_from("abl");
                                            }
                                        lab32: ;
                                            break;
                                        }
                                        case 2: {
                                            {
                                                int c20 = limit - cursor;
                                                if (I_p2 > cursor)
                                                {
                                                    goto lab35;
                                                }
                                                slice_del();
                                                goto lab34;
                                            lab35: ;
                                                cursor = limit - c20;
                                                slice_from("iqU");
                                            }
                                        lab34: ;
                                            break;
                                        }
                                        case 3: {
                                            if (I_p2 > cursor)
                                            {
                                                {
                                                    cursor = limit - c18;
                                                    goto lab31;
                                                }
                                            }
                                            slice_del();
                                            break;
                                        }
                                    }
                                lab31: ;
                                }
                                break;
                            }
                            case 8: {
                                if (I_p2 > cursor)
                                {
                                    goto lab24;
                                }
                                slice_del();
                                {
                                    int c21 = limit - cursor;
                                    ket = cursor;
                                    if (!(eq_s_b("at")))
                                    {
                                        {
                                            cursor = limit - c21;
                                            goto lab36;
                                        }
                                    }
                                    bra = cursor;
                                    if (I_p2 > cursor)
                                    {
                                        {
                                            cursor = limit - c21;
                                            goto lab36;
                                        }
                                    }
                                    slice_del();
                                    ket = cursor;
                                    if (!(eq_s_b("ic")))
                                    {
                                        {
                                            cursor = limit - c21;
                                            goto lab36;
                                        }
                                    }
                                    bra = cursor;
                                    {
                                        int c22 = limit - cursor;
                                        if (I_p2 > cursor)
                                        {
                                            goto lab38;
                                        }
                                        slice_del();
                                        goto lab37;
                                    lab38: ;
                                        cursor = limit - c22;
                                        slice_from("iqU");
                                    }
                                lab37: ;
                                lab36: ;
                                }
                                break;
                            }
                            case 9: {
                                slice_from("eau");
                                break;
                            }
                            case 10: {
                                if (I_p1 > cursor)
                                {
                                    goto lab24;
                                }
                                slice_from("al");
                                break;
                            }
                            case 11: {
                                if (in_grouping_b(g_oux_ending, 98, 112, false) != 0)
                                {
                                    goto lab24;
                                }
                                slice_from("ou");
                                break;
                            }
                            case 12: {
                                {
                                    int c23 = limit - cursor;
                                    if (I_p2 > cursor)
                                    {
                                        goto lab40;
                                    }
                                    slice_del();
                                    goto lab39;
                                lab40: ;
                                    cursor = limit - c23;
                                    if (I_p1 > cursor)
                                    {
                                        goto lab24;
                                    }
                                    slice_from("eux");
                                }
                            lab39: ;
                                break;
                            }
                            case 13: {
                                if (I_p1 > cursor)
                                {
                                    goto lab24;
                                }
                                if (out_grouping_b(g_v, 97, 251, false) != 0)
                                {
                                    goto lab24;
                                }
                                slice_del();
                                break;
                            }
                            case 14: {
                                if (I_pV > cursor)
                                {
                                    goto lab24;
                                }
                                slice_from("ant");
                                goto lab24;
                                break;
                            }
                            case 15: {
                                if (I_pV > cursor)
                                {
                                    goto lab24;
                                }
                                slice_from("ent");
                                goto lab24;
                                break;
                            }
                            case 16: {
                                {
                                    int c24 = limit - cursor;
                                    if (in_grouping_b(g_v, 97, 251, false) != 0)
                                    {
                                        goto lab24;
                                    }
                                    if (I_pV > cursor)
                                    {
                                        goto lab24;
                                    }
                                    cursor = limit - c24;
                                }
                                slice_del();
                                goto lab24;
                                break;
                            }
                        }
                        goto lab23;
                    lab24: ;
                        cursor = limit - c13;
                        if (cursor < I_pV)
                        {
                            goto lab41;
                        }
                        int c25 = limit_backward;
                        limit_backward = I_pV;
                        ket = cursor;
                        if (find_among_b(a_5, null) == 0)
                        {
                            {
                                limit_backward = c25;
                                goto lab41;
                            }
                        }
                        bra = cursor;
                        if (!(eq_s_b("H")))
                        {
                            goto lab42;
                        }
                        {
                            limit_backward = c25;
                            goto lab41;
                        }
                    lab42: ;
                        if (out_grouping_b(g_v, 97, 251, false) != 0)
                        {
                            {
                                limit_backward = c25;
                                goto lab41;
                            }
                        }
                        slice_del();
                        limit_backward = c25;
                        goto lab23;
                    lab41: ;
                        cursor = limit - c13;
                        if (cursor < I_pV)
                        {
                            goto lab22;
                        }
                        int c26 = limit_backward;
                        limit_backward = I_pV;
                        ket = cursor;
                        among_var = find_among_b(a_7, null);
                        if (among_var == 0)
                        {
                            {
                                limit_backward = c26;
                                goto lab22;
                            }
                        }
                        bra = cursor;
                        limit_backward = c26;
                        switch (among_var) {
                            case 1: {
                                if (I_p2 > cursor)
                                {
                                    goto lab22;
                                }
                                slice_del();
                                break;
                            }
                            case 2: {
                                slice_del();
                                break;
                            }
                            case 3: {
                                {
                                    int c27 = limit - cursor;
                                    if (!(eq_s_b("e")))
                                    {
                                        {
                                            cursor = limit - c27;
                                            goto lab43;
                                        }
                                    }
                                    if (I_pV > cursor)
                                    {
                                        {
                                            cursor = limit - c27;
                                            goto lab43;
                                        }
                                    }
                                    bra = cursor;
                                lab43: ;
                                }
                                slice_del();
                                break;
                            }
                            case 4: {
                                {
                                    int c28 = limit - cursor;
                                    among_var = find_among_b(a_6, null);
                                    if (among_var == 0)
                                    {
                                        goto lab44;
                                    }
                                    switch (among_var) {
                                        case 1: {
                                            if (cursor <= limit_backward)
                                            {
                                                goto lab44;
                                            }
                                            cursor--;
                                            if (cursor > limit_backward)
                                            {
                                                goto lab44;
                                            }
                                            break;
                                        }
                                    }
                                    goto lab22;
                                lab44: ;
                                    cursor = limit - c28;
                                }
                                slice_del();
                                break;
                            }
                        }
                    }
                lab23: ;
                    cursor = limit - c12;
                    {
                        int c29 = limit - cursor;
                        ket = cursor;
                        {
                            int c30 = limit - cursor;
                            if (!(eq_s_b("Y")))
                            {
                                goto lab47;
                            }
                            bra = cursor;
                            slice_from("i");
                            goto lab46;
                        lab47: ;
                            cursor = limit - c30;
                            if (!(eq_s_b("ç")))
                            {
                                {
                                    cursor = limit - c29;
                                    goto lab45;
                                }
                            }
                            bra = cursor;
                            slice_from("c");
                        }
                    lab46: ;
                    lab45: ;
                    }
                    goto lab21;
                lab22: ;
                    cursor = limit - c11;
                    {
                        int c31 = limit - cursor;
                        ket = cursor;
                        if (!(eq_s_b("s")))
                        {
                            {
                                cursor = limit - c31;
                                goto lab48;
                            }
                        }
                        bra = cursor;
                        {
                            int c32 = limit - cursor;
                            if (!(eq_s_b("Hi")))
                            {
                                goto lab50;
                            }
                            goto lab49;
                        lab50: ;
                            if (out_grouping_b(g_keep_with_s, 97, 232, false) != 0)
                            {
                                {
                                    cursor = limit - c31;
                                    goto lab48;
                                }
                            }
                        lab49: ;
                            cursor = limit - c32;
                        }
                        slice_del();
                    lab48: ;
                    }
                    if (cursor < I_pV)
                    {
                        goto lab20;
                    }
                    int c33 = limit_backward;
                    limit_backward = I_pV;
                    ket = cursor;
                    among_var = find_among_b(a_8, null);
                    if (among_var == 0)
                    {
                        {
                            limit_backward = c33;
                            goto lab20;
                        }
                    }
                    bra = cursor;
                    switch (among_var) {
                        case 1: {
                            if (I_p2 > cursor)
                            {
                                {
                                    limit_backward = c33;
                                    goto lab20;
                                }
                            }
                            if (!(eq_s_b("s")))
                            {
                                goto lab52;
                            }
                            goto lab51;
                        lab52: ;
                            if (!(eq_s_b("t")))
                            {
                                {
                                    limit_backward = c33;
                                    goto lab20;
                                }
                            }
                        lab51: ;
                            slice_del();
                            break;
                        }
                        case 2: {
                            slice_from("i");
                            break;
                        }
                        case 3: {
                            slice_del();
                            break;
                        }
                    }
                    limit_backward = c33;
                }
            lab21: ;
            lab20: ;
                cursor = limit - c10;
            }
            {
                int c34 = limit - cursor;
                {
                    int c35 = limit - cursor;
                    if (find_among_b(a_9, null) == 0)
                    {
                        goto lab53;
                    }
                    cursor = limit - c35;
                }
                ket = cursor;
                if (cursor <= limit_backward)
                {
                    goto lab53;
                }
                cursor--;
                bra = cursor;
                slice_del();
            lab53: ;
                cursor = limit - c34;
            }
            {
                int c36 = limit - cursor;
                {
                    int c37 = 1;
                    while (true)
                    {
                        if (out_grouping_b(g_v, 97, 251, false) != 0)
                        {
                            goto lab55;
                        }
                        c37--;
                        continue;
                    lab55: ;
                        break;
                    }
                    if (c37 > 0)
                    {
                        goto lab54;
                    }
                }
                ket = cursor;
                if (!(eq_s_b("é")))
                {
                    goto lab57;
                }
                goto lab56;
            lab57: ;
                if (!(eq_s_b("è")))
                {
                    goto lab54;
                }
            lab56: ;
                bra = cursor;
                slice_from("e");
            lab54: ;
                cursor = limit - c36;
            }
            cursor = limit_backward;
            {
                int c38 = cursor;
                while (true)
                {
                    int c39 = cursor;
                    bra = cursor;
                    among_var = find_among(a_1, null);
                    ket = cursor;
                    switch (among_var) {
                        case 1: {
                            slice_from("i");
                            break;
                        }
                        case 2: {
                            slice_from("u");
                            break;
                        }
                        case 3: {
                            slice_from("y");
                            break;
                        }
                        case 4: {
                            slice_from("ë");
                            break;
                        }
                        case 5: {
                            slice_from("ï");
                            break;
                        }
                        case 6: {
                            slice_del();
                            break;
                        }
                        case 7: {
                            if (cursor >= limit)
                            {
                                goto lab59;
                            }
                            cursor++;
                            break;
                        }
                    }
                    continue;
                lab59: ;
                    cursor = c39;
                    break;
                }
                cursor = c38;
            }
            return true;
        }

    }
}

