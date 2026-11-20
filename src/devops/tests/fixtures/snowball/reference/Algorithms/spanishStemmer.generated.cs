// Generated from spanish.sbl by Snowball 3.2.0 - https://snowballstem.org/

#pragma warning disable 0164
#pragma warning disable 0162

namespace Snowball
{
    using System;
    using System.Text;

    ///<summary>
    ///  This class implements the stemming algorithm defined by a snowball script.
    ///  Generated from spanish.sbl by Snowball 3.2.0 - https://snowballstem.org/
    ///</summary>
    ///
    [System.CodeDom.Compiler.GeneratedCode("Snowball", "3.2.0")]
    public partial class SpanishStemmer : Stemmer
    {
        private const string g_v = "aeiouáéíóúü";

        private static readonly Among[] a_0 = new[]
        {
            new Among("", -1, 6, 0),
            new Among("á", 0, 1, 0),
            new Among("é", 0, 2, 0),
            new Among("í", 0, 3, 0),
            new Among("ó", 0, 4, 0),
            new Among("ú", 0, 5, 0)
        };

        private static readonly Among[] a_1 = new[]
        {
            new Among("la", -1, -1, 0),
            new Among("sela", 0, -1, 0),
            new Among("le", -1, -1, 0),
            new Among("me", -1, -1, 0),
            new Among("se", -1, -1, 0),
            new Among("lo", -1, -1, 0),
            new Among("selo", 5, -1, 0),
            new Among("las", -1, -1, 0),
            new Among("selas", 7, -1, 0),
            new Among("les", -1, -1, 0),
            new Among("los", -1, -1, 0),
            new Among("selos", 10, -1, 0),
            new Among("nos", -1, -1, 0)
        };

        private static readonly Among[] a_2 = new[]
        {
            new Among("ando", -1, 6, 0),
            new Among("iendo", -1, 6, 0),
            new Among("yendo", -1, 7, 0),
            new Among("ándo", -1, 2, 0),
            new Among("iéndo", -1, 1, 0),
            new Among("ar", -1, 6, 0),
            new Among("er", -1, 6, 0),
            new Among("ir", -1, 6, 0),
            new Among("ár", -1, 3, 0),
            new Among("ér", -1, 4, 0),
            new Among("ír", -1, 5, 0)
        };

        private static readonly Among[] a_3 = new[]
        {
            new Among("ic", -1, -1, 0),
            new Among("ad", -1, -1, 0),
            new Among("os", -1, -1, 0),
            new Among("iv", -1, 1, 0)
        };

        private static readonly Among[] a_4 = new[]
        {
            new Among("able", -1, 1, 0),
            new Among("ible", -1, 1, 0),
            new Among("ante", -1, 1, 0)
        };

        private static readonly Among[] a_5 = new[]
        {
            new Among("ic", -1, 1, 0),
            new Among("abil", -1, 1, 0),
            new Among("iv", -1, 1, 0)
        };

        private static readonly Among[] a_6 = new[]
        {
            new Among("ica", -1, 1, 0),
            new Among("ancia", -1, 2, 0),
            new Among("encia", -1, 5, 0),
            new Among("adora", -1, 2, 0),
            new Among("osa", -1, 1, 0),
            new Among("ista", -1, 1, 0),
            new Among("iva", -1, 9, 0),
            new Among("anza", -1, 1, 0),
            new Among("logía", -1, 3, 0),
            new Among("idad", -1, 8, 0),
            new Among("able", -1, 1, 0),
            new Among("ible", -1, 1, 0),
            new Among("ante", -1, 2, 0),
            new Among("mente", -1, 7, 0),
            new Among("amente", 13, 6, 0),
            new Among("acion", -1, 2, 0),
            new Among("ucion", -1, 4, 0),
            new Among("ación", -1, 2, 0),
            new Among("ución", -1, 4, 0),
            new Among("ico", -1, 1, 0),
            new Among("ismo", -1, 1, 0),
            new Among("oso", -1, 1, 0),
            new Among("amiento", -1, 1, 0),
            new Among("imiento", -1, 1, 0),
            new Among("ivo", -1, 9, 0),
            new Among("ador", -1, 2, 0),
            new Among("icas", -1, 1, 0),
            new Among("ancias", -1, 2, 0),
            new Among("encias", -1, 5, 0),
            new Among("adoras", -1, 2, 0),
            new Among("osas", -1, 1, 0),
            new Among("istas", -1, 1, 0),
            new Among("ivas", -1, 9, 0),
            new Among("anzas", -1, 1, 0),
            new Among("logías", -1, 3, 0),
            new Among("idades", -1, 8, 0),
            new Among("ables", -1, 1, 0),
            new Among("ibles", -1, 1, 0),
            new Among("aciones", -1, 2, 0),
            new Among("uciones", -1, 4, 0),
            new Among("adores", -1, 2, 0),
            new Among("antes", -1, 2, 0),
            new Among("icos", -1, 1, 0),
            new Among("ismos", -1, 1, 0),
            new Among("osos", -1, 1, 0),
            new Among("amientos", -1, 1, 0),
            new Among("imientos", -1, 1, 0),
            new Among("ivos", -1, 9, 0)
        };

        private static readonly Among[] a_7 = new[]
        {
            new Among("ya", -1, 1, 0),
            new Among("ye", -1, 1, 0),
            new Among("yan", -1, 1, 0),
            new Among("yen", -1, 1, 0),
            new Among("yeron", -1, 1, 0),
            new Among("yendo", -1, 1, 0),
            new Among("yo", -1, 1, 0),
            new Among("yas", -1, 1, 0),
            new Among("yes", -1, 1, 0),
            new Among("yais", -1, 1, 0),
            new Among("yamos", -1, 1, 0),
            new Among("yó", -1, 1, 0)
        };

        private static readonly Among[] a_8 = new[]
        {
            new Among("aba", -1, 2, 0),
            new Among("ada", -1, 2, 0),
            new Among("ida", -1, 2, 0),
            new Among("ara", -1, 2, 0),
            new Among("iera", -1, 2, 0),
            new Among("ía", -1, 2, 0),
            new Among("aría", 5, 2, 0),
            new Among("ería", 5, 2, 0),
            new Among("iría", 5, 2, 0),
            new Among("ad", -1, 2, 0),
            new Among("ed", -1, 2, 0),
            new Among("id", -1, 2, 0),
            new Among("ase", -1, 2, 0),
            new Among("iese", -1, 2, 0),
            new Among("aste", -1, 2, 0),
            new Among("iste", -1, 2, 0),
            new Among("an", -1, 2, 0),
            new Among("aban", 16, 2, 0),
            new Among("aran", 16, 2, 0),
            new Among("ieran", 16, 2, 0),
            new Among("ían", 16, 2, 0),
            new Among("arían", 20, 2, 0),
            new Among("erían", 20, 2, 0),
            new Among("irían", 20, 2, 0),
            new Among("en", -1, 1, 0),
            new Among("asen", 24, 2, 0),
            new Among("iesen", 24, 2, 0),
            new Among("aron", -1, 2, 0),
            new Among("ieron", -1, 2, 0),
            new Among("arán", -1, 2, 0),
            new Among("erán", -1, 2, 0),
            new Among("irán", -1, 2, 0),
            new Among("ado", -1, 2, 0),
            new Among("ido", -1, 2, 0),
            new Among("ando", -1, 2, 0),
            new Among("iendo", -1, 2, 0),
            new Among("ar", -1, 2, 0),
            new Among("er", -1, 2, 0),
            new Among("ir", -1, 2, 0),
            new Among("as", -1, 2, 0),
            new Among("abas", 39, 2, 0),
            new Among("adas", 39, 2, 0),
            new Among("idas", 39, 2, 0),
            new Among("aras", 39, 2, 0),
            new Among("ieras", 39, 2, 0),
            new Among("ías", 39, 2, 0),
            new Among("arías", 45, 2, 0),
            new Among("erías", 45, 2, 0),
            new Among("irías", 45, 2, 0),
            new Among("es", -1, 1, 0),
            new Among("ases", 49, 2, 0),
            new Among("ieses", 49, 2, 0),
            new Among("abais", -1, 2, 0),
            new Among("arais", -1, 2, 0),
            new Among("ierais", -1, 2, 0),
            new Among("íais", -1, 2, 0),
            new Among("aríais", 55, 2, 0),
            new Among("eríais", 55, 2, 0),
            new Among("iríais", 55, 2, 0),
            new Among("aseis", -1, 2, 0),
            new Among("ieseis", -1, 2, 0),
            new Among("asteis", -1, 2, 0),
            new Among("isteis", -1, 2, 0),
            new Among("áis", -1, 2, 0),
            new Among("éis", -1, 1, 0),
            new Among("aréis", 64, 2, 0),
            new Among("eréis", 64, 2, 0),
            new Among("iréis", 64, 2, 0),
            new Among("ados", -1, 2, 0),
            new Among("idos", -1, 2, 0),
            new Among("amos", -1, 2, 0),
            new Among("ábamos", 70, 2, 0),
            new Among("áramos", 70, 2, 0),
            new Among("iéramos", 70, 2, 0),
            new Among("íamos", 70, 2, 0),
            new Among("aríamos", 74, 2, 0),
            new Among("eríamos", 74, 2, 0),
            new Among("iríamos", 74, 2, 0),
            new Among("emos", -1, 1, 0),
            new Among("aremos", 78, 2, 0),
            new Among("eremos", 78, 2, 0),
            new Among("iremos", 78, 2, 0),
            new Among("ásemos", 78, 2, 0),
            new Among("iésemos", 78, 2, 0),
            new Among("imos", -1, 2, 0),
            new Among("arás", -1, 2, 0),
            new Among("erás", -1, 2, 0),
            new Among("irás", -1, 2, 0),
            new Among("ís", -1, 2, 0),
            new Among("ará", -1, 2, 0),
            new Among("erá", -1, 2, 0),
            new Among("irá", -1, 2, 0),
            new Among("aré", -1, 2, 0),
            new Among("eré", -1, 2, 0),
            new Among("iré", -1, 2, 0),
            new Among("ió", -1, 2, 0)
        };

        private static readonly Among[] a_9 = new[]
        {
            new Among("a", -1, 1, 0),
            new Among("e", -1, 2, 0),
            new Among("o", -1, 1, 0),
            new Among("os", -1, 1, 0),
            new Among("á", -1, 1, 0),
            new Among("é", -1, 2, 0),
            new Among("í", -1, 1, 0),
            new Among("ó", -1, 1, 0)
        };


        protected override bool stem()
        {
            int among_var;
            int I_p2;
            int I_p1;
            int I_pV;
            I_pV = limit;
            I_p1 = limit;
            I_p2 = limit;
            {
                int c1 = cursor;
                {
                    int c2 = cursor;
                    if (in_grouping(g_v, 97, 252, false) != 0)
                    {
                        goto lab3;
                    }
                    {
                        int c3 = cursor;
                        if (out_grouping(g_v, 97, 252, false) != 0)
                        {
                            goto lab5;
                        }
                        {

                            int ret = out_grouping(g_v, 97, 252, true);
                            if (ret < 0)
                            {
                                goto lab5;
                            }

                            cursor += ret;
                        }
                        goto lab4;
                    lab5: ;
                        cursor = c3;
                        if (in_grouping(g_v, 97, 252, false) != 0)
                        {
                            goto lab3;
                        }
                        {

                            int ret = in_grouping(g_v, 97, 252, true);
                            if (ret < 0)
                            {
                                goto lab3;
                            }

                            cursor += ret;
                        }
                    }
                lab4: ;
                    goto lab2;
                lab3: ;
                    cursor = c2;
                    if (out_grouping(g_v, 97, 252, false) != 0)
                    {
                        goto lab1;
                    }
                    {
                        int c4 = cursor;
                        if (out_grouping(g_v, 97, 252, false) != 0)
                        {
                            goto lab7;
                        }
                        {

                            int ret = out_grouping(g_v, 97, 252, true);
                            if (ret < 0)
                            {
                                goto lab7;
                            }

                            cursor += ret;
                        }
                        goto lab6;
                    lab7: ;
                        cursor = c4;
                        if (in_grouping(g_v, 97, 252, false) != 0)
                        {
                            goto lab1;
                        }
                        if (cursor >= limit)
                        {
                            goto lab1;
                        }
                        cursor++;
                    }
                lab6: ;
                }
            lab2: ;
                I_pV = cursor;
            lab1: ;
                cursor = c1;
            }
            {
                int c5 = cursor;
                {

                    int ret = out_grouping(g_v, 97, 252, true);
                    if (ret < 0)
                    {
                        goto lab8;
                    }

                    cursor += ret;
                }
                {

                    int ret = in_grouping(g_v, 97, 252, true);
                    if (ret < 0)
                    {
                        goto lab8;
                    }

                    cursor += ret;
                }
                I_p1 = cursor;
                {

                    int ret = out_grouping(g_v, 97, 252, true);
                    if (ret < 0)
                    {
                        goto lab8;
                    }

                    cursor += ret;
                }
                {

                    int ret = in_grouping(g_v, 97, 252, true);
                    if (ret < 0)
                    {
                        goto lab8;
                    }

                    cursor += ret;
                }
                I_p2 = cursor;
            lab8: ;
                cursor = c5;
            }
            limit_backward = cursor;
            cursor = limit;
            {
                int c6 = limit - cursor;
                ket = cursor;
                if (find_among_b(a_1, null) == 0)
                {
                    goto lab9;
                }
                bra = cursor;
                among_var = find_among_b(a_2, null);
                if (among_var == 0)
                {
                    goto lab9;
                }
                if (I_pV > cursor)
                {
                    goto lab9;
                }
                switch (among_var) {
                    case 1: {
                        bra = cursor;
                        slice_from("iendo");
                        break;
                    }
                    case 2: {
                        bra = cursor;
                        slice_from("ando");
                        break;
                    }
                    case 3: {
                        bra = cursor;
                        slice_from("ar");
                        break;
                    }
                    case 4: {
                        bra = cursor;
                        slice_from("er");
                        break;
                    }
                    case 5: {
                        bra = cursor;
                        slice_from("ir");
                        break;
                    }
                    case 6: {
                        slice_del();
                        break;
                    }
                    case 7: {
                        if (!(eq_s_b("u")))
                        {
                            goto lab9;
                        }
                        slice_del();
                        break;
                    }
                }
            lab9: ;
                cursor = limit - c6;
            }
            {
                int c7 = limit - cursor;
                {
                    int c8 = limit - cursor;
                    ket = cursor;
                    among_var = find_among_b(a_6, null);
                    if (among_var == 0)
                    {
                        goto lab12;
                    }
                    bra = cursor;
                    switch (among_var) {
                        case 1: {
                            if (I_p2 > cursor)
                            {
                                goto lab12;
                            }
                            slice_del();
                            break;
                        }
                        case 2: {
                            if (I_p2 > cursor)
                            {
                                goto lab12;
                            }
                            slice_del();
                            {
                                int c9 = limit - cursor;
                                ket = cursor;
                                if (!(eq_s_b("ic")))
                                {
                                    {
                                        cursor = limit - c9;
                                        goto lab13;
                                    }
                                }
                                bra = cursor;
                                if (I_p2 > cursor)
                                {
                                    {
                                        cursor = limit - c9;
                                        goto lab13;
                                    }
                                }
                                slice_del();
                            lab13: ;
                            }
                            break;
                        }
                        case 3: {
                            if (I_p2 > cursor)
                            {
                                goto lab12;
                            }
                            slice_from("log");
                            break;
                        }
                        case 4: {
                            if (I_p2 > cursor)
                            {
                                goto lab12;
                            }
                            slice_from("u");
                            break;
                        }
                        case 5: {
                            if (I_p2 > cursor)
                            {
                                goto lab12;
                            }
                            slice_from("ente");
                            break;
                        }
                        case 6: {
                            if (I_p1 > cursor)
                            {
                                goto lab12;
                            }
                            slice_del();
                            {
                                int c10 = limit - cursor;
                                ket = cursor;
                                among_var = find_among_b(a_3, null);
                                if (among_var == 0)
                                {
                                    {
                                        cursor = limit - c10;
                                        goto lab14;
                                    }
                                }
                                bra = cursor;
                                if (I_p2 > cursor)
                                {
                                    {
                                        cursor = limit - c10;
                                        goto lab14;
                                    }
                                }
                                slice_del();
                                switch (among_var) {
                                    case 1: {
                                        ket = cursor;
                                        if (!(eq_s_b("at")))
                                        {
                                            {
                                                cursor = limit - c10;
                                                goto lab14;
                                            }
                                        }
                                        bra = cursor;
                                        if (I_p2 > cursor)
                                        {
                                            {
                                                cursor = limit - c10;
                                                goto lab14;
                                            }
                                        }
                                        slice_del();
                                        break;
                                    }
                                }
                            lab14: ;
                            }
                            break;
                        }
                        case 7: {
                            if (I_p2 > cursor)
                            {
                                goto lab12;
                            }
                            slice_del();
                            {
                                int c11 = limit - cursor;
                                ket = cursor;
                                if (find_among_b(a_4, null) == 0)
                                {
                                    {
                                        cursor = limit - c11;
                                        goto lab15;
                                    }
                                }
                                bra = cursor;
                                if (I_p2 > cursor)
                                {
                                    {
                                        cursor = limit - c11;
                                        goto lab15;
                                    }
                                }
                                slice_del();
                            lab15: ;
                            }
                            break;
                        }
                        case 8: {
                            if (I_p2 > cursor)
                            {
                                goto lab12;
                            }
                            slice_del();
                            {
                                int c12 = limit - cursor;
                                ket = cursor;
                                if (find_among_b(a_5, null) == 0)
                                {
                                    {
                                        cursor = limit - c12;
                                        goto lab16;
                                    }
                                }
                                bra = cursor;
                                if (I_p2 > cursor)
                                {
                                    {
                                        cursor = limit - c12;
                                        goto lab16;
                                    }
                                }
                                slice_del();
                            lab16: ;
                            }
                            break;
                        }
                        case 9: {
                            if (I_p2 > cursor)
                            {
                                goto lab12;
                            }
                            slice_del();
                            {
                                int c13 = limit - cursor;
                                ket = cursor;
                                if (!(eq_s_b("at")))
                                {
                                    {
                                        cursor = limit - c13;
                                        goto lab17;
                                    }
                                }
                                bra = cursor;
                                if (I_p2 > cursor)
                                {
                                    {
                                        cursor = limit - c13;
                                        goto lab17;
                                    }
                                }
                                slice_del();
                            lab17: ;
                            }
                            break;
                        }
                    }
                    goto lab11;
                lab12: ;
                    cursor = limit - c8;
                    if (cursor < I_pV)
                    {
                        goto lab18;
                    }
                    int c14 = limit_backward;
                    limit_backward = I_pV;
                    ket = cursor;
                    if (find_among_b(a_7, null) == 0)
                    {
                        {
                            limit_backward = c14;
                            goto lab18;
                        }
                    }
                    bra = cursor;
                    limit_backward = c14;
                    if (!(eq_s_b("u")))
                    {
                        goto lab18;
                    }
                    slice_del();
                    goto lab11;
                lab18: ;
                    cursor = limit - c8;
                    if (cursor < I_pV)
                    {
                        goto lab10;
                    }
                    int c15 = limit_backward;
                    limit_backward = I_pV;
                    ket = cursor;
                    among_var = find_among_b(a_8, null);
                    if (among_var == 0)
                    {
                        {
                            limit_backward = c15;
                            goto lab10;
                        }
                    }
                    bra = cursor;
                    limit_backward = c15;
                    switch (among_var) {
                        case 1: {
                            {
                                int c16 = limit - cursor;
                                if (!(eq_s_b("u")))
                                {
                                    {
                                        cursor = limit - c16;
                                        goto lab19;
                                    }
                                }
                                {
                                    int c17 = limit - cursor;
                                    if (!(eq_s_b("g")))
                                    {
                                        {
                                            cursor = limit - c16;
                                            goto lab19;
                                        }
                                    }
                                    cursor = limit - c17;
                                }
                            lab19: ;
                            }
                            bra = cursor;
                            slice_del();
                            break;
                        }
                        case 2: {
                            slice_del();
                            break;
                        }
                    }
                }
            lab11: ;
            lab10: ;
                cursor = limit - c7;
            }
            {
                int c18 = limit - cursor;
                ket = cursor;
                among_var = find_among_b(a_9, null);
                if (among_var == 0)
                {
                    goto lab20;
                }
                bra = cursor;
                switch (among_var) {
                    case 1: {
                        if (I_pV > cursor)
                        {
                            goto lab20;
                        }
                        slice_del();
                        break;
                    }
                    case 2: {
                        if (I_pV > cursor)
                        {
                            goto lab20;
                        }
                        slice_del();
                        {
                            int c19 = limit - cursor;
                            ket = cursor;
                            if (!(eq_s_b("u")))
                            {
                                {
                                    cursor = limit - c19;
                                    goto lab21;
                                }
                            }
                            bra = cursor;
                            {
                                int c20 = limit - cursor;
                                if (!(eq_s_b("g")))
                                {
                                    {
                                        cursor = limit - c19;
                                        goto lab21;
                                    }
                                }
                                cursor = limit - c20;
                            }
                            if (I_pV > cursor)
                            {
                                {
                                    cursor = limit - c19;
                                    goto lab21;
                                }
                            }
                            slice_del();
                        lab21: ;
                        }
                        break;
                    }
                }
            lab20: ;
                cursor = limit - c18;
            }
            cursor = limit_backward;
            {
                int c21 = cursor;
                while (true)
                {
                    int c22 = cursor;
                    bra = cursor;
                    among_var = find_among(a_0, null);
                    ket = cursor;
                    switch (among_var) {
                        case 1: {
                            slice_from("a");
                            break;
                        }
                        case 2: {
                            slice_from("e");
                            break;
                        }
                        case 3: {
                            slice_from("i");
                            break;
                        }
                        case 4: {
                            slice_from("o");
                            break;
                        }
                        case 5: {
                            slice_from("u");
                            break;
                        }
                        case 6: {
                            if (cursor >= limit)
                            {
                                goto lab23;
                            }
                            cursor++;
                            break;
                        }
                    }
                    continue;
                lab23: ;
                    cursor = c22;
                    break;
                }
                cursor = c21;
            }
            return true;
        }

    }
}

