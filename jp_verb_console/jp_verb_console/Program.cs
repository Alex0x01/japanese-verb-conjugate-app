using System;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using System.Timers;
using System.Windows.Input;
using System.Runtime.InteropServices;

namespace jp_verb_console
{
    internal class Program
    {
        #region JSON_DATA
        public const int iMAX_ENGLISH_VERB = 6;

        // this lot is for JSON serialization, if you change it, it will probably all break.
        //=================================================================================================
        public class Verb_Data
        {
            public string desc { get; set; }        // description in english
            public string kanji { get; set; }       // kanji form
            public string kana { get; set; }        // kana form
            public string[] en { get; set; }        // list of acceptable english answers, e.g. [masen, masenn] - *note* all spaces are stripped from these and user input when comparing
        }

        public class Verb_LanguageType
        {
            public Verb_Data positive { get; set; }
            public Verb_Data negative { get; set; }
        };

        public class Verb_Form
        {
            public string name { get; set; } // e.g. "past presumprive", "present indicative" etc.
            public Verb_LanguageType casual { get; set; }
            public Verb_LanguageType formal { get; set; }
        };

        // json data is an array of these verbs
        public class Verb_Obj
        {
            public string name { get; set; }            // the name of the verb, e.g. "to go"
            public string[] class_type { get; set; }    // this is info for the verb classes, e.g. "intransitive"
            public Verb_Data te_form { get; set; }      // all verbs have one te form, this is an exception, so can't be added in "forms"
            public Verb_Form[] forms { get; set; }      // an array of the different verb conjugates, easy to add more in json. e.g. "past presumptive"
        }

        // essentially... this is all of the JSON data clumped into this
        public class Verb_Arr
        {
            public Verb_Obj[] verbs { get; set; }
        };
        #endregion
        //=================================================================================================

        public enum E_GameMode
        {
            Random,           // randomly pick from everything
            All,              // do everything from top to bottom
            SingleFormat,     // pass in one format, and do that single format for each verb e.g. te form
            SingleAll,        // same as single format but loops through each format
            RepeatVerb,       // just do the same one over and over, require -repeat_verb
            ReviseMode,
            _E_NumGameModes
        };

        // ignoring my usual naming convention of enums for json deserialization.
        public enum E_VerbFormat
        {
            NULL = -1,
            casual_positive,    // all verb conjugates will have have this (excluding te form)
            casual_negative,    // all verb conjugates will have have this (excluding te form)
            formal_positive,    // all verb conjugates will have have this (excluding te form)
            formal_negative,    // all verb conjugates will have have this (excluding te form)
            te_form,            // te form is a weird exception so it has to be "treat" differently
            _num_formats
        }

        // a clump of data, with defaults, we can add args to the cmd line to set these. pass -help to see.
        public class CmdArgs
        {
            public List<int> iSingleVerbIndexes = new List<int>();
            public E_VerbFormat eSingleFormatMode = E_VerbFormat.casual_positive;
            public E_GameMode eGameMode = E_GameMode.SingleAll;
            public int iNumQuestions = 10;
            
            // ...etc, add whatever
        };

        // this blob of data is re-used for each "game tick", it is built based on the game mode and other various data.
        public class Question
        {
            public int iVerbIndex = 0;
            public int iVerbFormIndex = 0;
            public E_VerbFormat eFormatType = E_VerbFormat.NULL;

            public Question()
            {
            }

            public Question( int verbIndex, int verbFormIndex, E_VerbFormat formatType )
            {
                this.iVerbIndex = verbIndex;
                this.iVerbFormIndex = verbFormIndex;
                this.eFormatType = formatType;
            }

            public Question( Question rhs )
            {
                this.iVerbIndex = rhs.iVerbIndex;
                this.iVerbFormIndex = rhs.iVerbFormIndex;
                this.eFormatType = rhs.eFormatType;
            }
        };
        //=================================================================================================

        public class GameResult
        {
            public bool bResult = false;
            public int iNumQuestionsCorrect = 0;
            public int iNumQuestionsIncorrect = 0;
            public string strResultMsg = string.Empty;
        };

        public static List<Question> s_reviseList = new List<Question>();

        static public GameResult MainLoop( CmdArgs cmdArgs, Verb_Arr xVerbs )
        {
            // random seeds
            int iNUM_SEEDS = 3;
            Random[] randoms = new Random[iNUM_SEEDS];
            for ( int i = 0; i < iNUM_SEEDS; ++i )
                randoms[i] = new Random();
            Random seed = new Random();

            // Verb_Data - kanji, kana, + possible english
            const int iNUM_POSSIBLE_ANSWERS = 2 + iMAX_ENGLISH_VERB;// 4; 

            int iMaxPossibleQuestions = xVerbs.verbs.Length; // this counts for the te-forms
            for ( int i = 0; i < xVerbs.verbs.Length; ++i )
            {
                // * times 4, because each verb has formal-positive, formal-negative, informal-positive, informal-negative
                iMaxPossibleQuestions += xVerbs.verbs[i].forms.Length * 4;
            }

            // init stuff here
            string[] answers = new string[iNUM_POSSIBLE_ANSWERS];
            for( int i = 0; i < iNUM_POSSIBLE_ANSWERS; ++i )
                answers[i] = string.Empty;

            // used in random mode, to break the loop (based on numQuestions)
            int iLoopIter = 1;
            // this is used for index bounds, it's cached on the first question, because of ordering
            int iCachedCurrentVerbNumTypes = 0;
            int iCachedReviseListSize = 0;
            int iReviseListIndex = 0;

            // don't repeat the questions in random mode with this
            List<Question> doneList = new List<Question>();
            List<Question> todoList = new List<Question>(); // built from revise list

            // for japanese chars
            Console.OutputEncoding = System.Text.Encoding.Unicode;
            Console.InputEncoding = System.Text.Encoding.Unicode;

            // build this up each iteration from game mode, and re-use it
            Question question = new Question();
            GameResult result = new GameResult();

            const string strQuit1 = "quitgame", strQuit2 = "exitgame";

            // display game mode settings (could be from cmd line)
            {
                Console.WriteLine( "Game Mode : {0}", cmdArgs.eGameMode.ToString() );
                if ( cmdArgs.eGameMode == E_GameMode.Random )
                    Console.WriteLine( "Num Questions: {0}", cmdArgs.iNumQuestions );
                if ( cmdArgs.eGameMode == E_GameMode.SingleFormat )
                    Console.WriteLine( "Single format: {0}", cmdArgs.eSingleFormatMode.ToString() );
                if ( cmdArgs.eGameMode == E_GameMode.RepeatVerb )
                {
                    for ( int v = 0; v < cmdArgs.iSingleVerbIndexes.Count; ++v )
                    {
                        int idx = cmdArgs.iSingleVerbIndexes[v];
                        if ( idx >= xVerbs.verbs.Length )
                        {
                            result.bResult = false;
                            result.strResultMsg = string.Format( "Error with index for single verb: {0}, should be less than: {1}.",
                                idx, xVerbs.verbs.Length );
                            return result;
                        }
                        Console.WriteLine( "Repeated verb: {0}", idx );
                    }
                }
                Console.WriteLine( "--------------------------------------------------------------------" );
                Console.WriteLine( "End game with: {0} or {1}", strQuit1, strQuit2 );
                Console.WriteLine( "--------------------------------------------------------------------\n" );
            }

            // Load the revise list
            if ( cmdArgs.eGameMode == E_GameMode.ReviseMode )
            {
                Task<string[]> task = LoadReviseListAsync( "revise_list.txt" );
                while ( !task.IsCompleted )
                {
                }

                if ( !task.IsFaulted )
                {
                    string[] lines = task.GetAwaiter().GetResult();
                    iCachedReviseListSize = lines.Length;
                    for ( int i = 0; i < lines.Length; ++i )
                    {
                        string[] spl = lines[i].Split( ":" );
                        if ( spl.Length != 2 )
                        {
                            continue;
                        }

                        string[] q = spl[0].Split( "," );
                        if ( q.Length != 3 )
                        {
                            continue;
                        }

                        int iVerbIndex = Convert.ToInt32( q[0] );
                        int iVerbFormIndex = Convert.ToInt32( q[1] );
                        int iType = Convert.ToInt32( q[2] );
                        todoList.Add( new Question( iVerbIndex, iVerbFormIndex, (E_VerbFormat)iType ) );
                    }
                }
            }

            while ( true )
            {
                // 1) calc question index based on game mode
                if ( cmdArgs.eGameMode == E_GameMode.Random )
                {
                    if ( iLoopIter > cmdArgs.iNumQuestions )
                    {
                        result.iNumQuestionsIncorrect = s_reviseList.Count;
                        result.iNumQuestionsCorrect = cmdArgs.iNumQuestions - result.iNumQuestionsIncorrect;
                        result.strResultMsg = "All Questions completed.";
                        result.bResult = true;
                        // done
                        break;
                    }

                    question.iVerbIndex = randoms[seed.Next( iNUM_SEEDS )].Next( xVerbs.verbs.Length );
                    question.eFormatType = (E_VerbFormat)randoms[seed.Next( iNUM_SEEDS )].Next( (int)E_VerbFormat._num_formats );
                    if ( question.eFormatType == E_VerbFormat.te_form )
                        question.iVerbFormIndex = -1;
                    else
                        question.iVerbFormIndex = randoms[seed.Next( iNUM_SEEDS )].Next( xVerbs.verbs[question.iVerbIndex].forms.Length );

                    // re-roll
                    int iAttempts = 2048;
                    while ( doneList.Contains( question ) )
                    {
                        if ( doneList.Count >= iMaxPossibleQuestions || ( --iAttempts <= 0 ) )
                        {
                            // we've done all of the questions, or have given up
                            doneList.Clear();
                        }

                        question.iVerbIndex = randoms[seed.Next( iNUM_SEEDS )].Next( xVerbs.verbs.Length );
                        question.eFormatType = (E_VerbFormat)randoms[seed.Next( iNUM_SEEDS )].Next( (int)E_VerbFormat._num_formats );
                        if ( question.eFormatType == E_VerbFormat.te_form )
                            question.iVerbFormIndex = -1;
                        else
                            question.iVerbFormIndex = randoms[seed.Next( iNUM_SEEDS )].Next( xVerbs.verbs[question.iVerbIndex].forms.Length );
                    }

                    // add to the done list then
                    doneList.Add( new Question( question ) ); // use implicit copy ctor, stop shitty c# using references
                }
                else if ( cmdArgs.eGameMode == E_GameMode.All )
                {
                    if ( iLoopIter == 1 )
                    {
                        question.eFormatType = E_VerbFormat.casual_positive;
                        question.iVerbFormIndex = 0;
                    }
                    else
                    {
                        if ( question.eFormatType == E_VerbFormat.te_form )
                        {
                            // reset
                            question.eFormatType = E_VerbFormat.casual_positive;
                            question.iVerbFormIndex = 0;

                            // inc the verb now
                            ++question.iVerbIndex;

                            // look for game over
                            if ( question.iVerbIndex >= xVerbs.verbs.Length )
                            {
                                result.bResult = true;
                                result.strResultMsg = "All questions completed";
                                result.iNumQuestionsIncorrect = s_reviseList.Count;
                                result.iNumQuestionsCorrect = iMaxPossibleQuestions - s_reviseList.Count;
                                // done
                                break;
                            }
                        }
                        else
                        {
                            ++question.iVerbFormIndex;
                            if ( question.iVerbFormIndex >= iCachedCurrentVerbNumTypes )
                            {
                                question.iVerbFormIndex = 0;
                                ++question.eFormatType;
                                if ( question.eFormatType >= E_VerbFormat._num_formats )
                                {
                                    // reset
                                    question.eFormatType = E_VerbFormat.casual_positive;
                                }
                            }
                        }
                    }
                }
                else if ( cmdArgs.eGameMode == E_GameMode.SingleFormat )
                {
                    question.eFormatType = cmdArgs.eSingleFormatMode;
                    bool bIncVerb = false;

                    if ( question.eFormatType == E_VerbFormat.te_form )
                    {
                        question.iVerbFormIndex = -1;
                        bIncVerb = true;
                    }
                    else
                    {
                        if ( iLoopIter > 1 )
                        {
                            ++question.iVerbFormIndex;
                            if ( question.iVerbFormIndex >= iCachedCurrentVerbNumTypes )
                            {
                                bIncVerb = true;
                                question.iVerbFormIndex = 0;
                            }
                        }
                    }

                    if ( iLoopIter > 1 && bIncVerb )
                    {
                        ++question.iVerbIndex;
                        if ( question.iVerbIndex >= xVerbs.verbs.Length )
                        {
                            result.bResult = true;
                            result.strResultMsg = "All single format questions completed";
                            result.iNumQuestionsIncorrect = s_reviseList.Count;

                            // none te form has both positive and negative so * 2
                            int iMaxSingleQuestions = question.eFormatType == E_VerbFormat.te_form
                                ? xVerbs.verbs.Length
                                : xVerbs.verbs.Length * 2;
                            result.iNumQuestionsCorrect = iMaxSingleQuestions - s_reviseList.Count;
                            // done
                            break;
                        }
                    }
                }
                else if ( cmdArgs.eGameMode == E_GameMode.SingleAll )
                {
                    if ( question.eFormatType == E_VerbFormat.NULL )
                        ++question.eFormatType;

                    bool bIncVerb = false;

                    if ( question.eFormatType == E_VerbFormat.te_form )
                    {
                        question.iVerbFormIndex = -1;
                        bIncVerb = true;
                    }
                    if ( iLoopIter > 1 )
                    {
                        ++question.iVerbFormIndex;
                        if ( question.iVerbFormIndex >= iCachedCurrentVerbNumTypes )
                        {
                            bIncVerb = true;
                            question.iVerbFormIndex = 0;
                        }
                    }

                    if ( iLoopIter > 1 && bIncVerb )
                    {
                        ++question.iVerbIndex;
                        if ( question.iVerbIndex >= xVerbs.verbs.Length )
                        {
                            question.iVerbIndex = 0;
                            ++question.eFormatType;
                            if ( question.eFormatType >= E_VerbFormat._num_formats )
                            {
                                result.bResult = true;
                                result.strResultMsg = "All questions completed";
                                result.iNumQuestionsIncorrect = s_reviseList.Count;
                                result.iNumQuestionsCorrect = iMaxPossibleQuestions - s_reviseList.Count;
                                // done
                                break;
                            }
                        }
                    }
                }
                else if ( cmdArgs.eGameMode == E_GameMode.RepeatVerb )
                {
                    if ( cmdArgs.iSingleVerbIndexes.Count == 0 )
                    {
                        result.bResult = false;
                        result.strResultMsg = "No verbs have been set to repeat";
                        return result;
                    }

                    // we can reuse iReviseListIndex
                    question.iVerbIndex = cmdArgs.iSingleVerbIndexes[iReviseListIndex];
                    
                    if ( iLoopIter == 1 )
                    {
                        question.eFormatType = E_VerbFormat.casual_positive;
                        question.iVerbFormIndex = 0;
                    }
                    else
                    {
                        bool bIsTeForm = question.eFormatType == E_VerbFormat.te_form;

                        ++question.iVerbFormIndex;
                        if ( (!bIsTeForm && question.iVerbFormIndex >= iCachedCurrentVerbNumTypes) || bIsTeForm )
                        {
                            question.iVerbFormIndex = 0;
                            ++question.eFormatType;
                        }

                        if ( question.eFormatType >= E_VerbFormat._num_formats )
                        {
                            // reset
                            question.eFormatType = E_VerbFormat.casual_positive;
                            
                            ++iReviseListIndex;
                            if ( iReviseListIndex >= cmdArgs.iSingleVerbIndexes.Count )
                            {
                                iReviseListIndex = 0;
                            }
                            question.iVerbIndex = cmdArgs.iSingleVerbIndexes[iReviseListIndex];
                        }
                    }
                }
                else if ( cmdArgs.eGameMode == E_GameMode.ReviseMode )
                {
                    if ( todoList.Count == 0 || iReviseListIndex >= todoList.Count )
                    {
                        result.bResult = true;
                        result.strResultMsg = "Nothing left in revise list";
                        result.iNumQuestionsIncorrect = s_reviseList.Count;
                        result.iNumQuestionsCorrect = iCachedReviseListSize - s_reviseList.Count;
                        break;
                    }

                    question.iVerbIndex = todoList[iReviseListIndex].iVerbIndex;
                    question.iVerbFormIndex = todoList[iReviseListIndex].iVerbFormIndex;
                    question.eFormatType = todoList[iReviseListIndex].eFormatType;
                    ++iReviseListIndex;
                }
                else
                {
                    result.bResult = false;
                    result.strResultMsg = "Error: unhandled game mode";
                    return result;
                }
                
                // shortcuts
                Verb_Obj verb = xVerbs.verbs[question.iVerbIndex];
                Verb_Form[] arrForms = verb.forms;
                int iNUM_VERB_FORMS = arrForms.Length;
                iCachedCurrentVerbNumTypes = iNUM_VERB_FORMS;

                // 2) build question strings from the data
                Verb_Data currentVerbData = null;
                string currentFormName = string.Empty;
                string currentFormDesc = "";

                if ( question.eFormatType == E_VerbFormat.te_form )
                {
                    // te form
                    currentVerbData = verb.te_form;
                    currentFormName = "[te form]";
                }
                else
                {
                    Verb_Form vf = arrForms[question.iVerbFormIndex];
                    string formName = string.Format("[{0}]", vf.name );
                    currentFormName = formName.Replace( "_", " " );
                    
                    switch ( question.eFormatType )
                    {
                        case E_VerbFormat.casual_positive: currentVerbData = vf.casual.positive; break;  
                        case E_VerbFormat.casual_negative: currentVerbData = vf.casual.negative; break;
                        case E_VerbFormat.formal_positive: currentVerbData = vf.formal.positive; break;
                        case E_VerbFormat.formal_negative: currentVerbData = vf.formal.negative; break;
                        default: break;
                    }

                    currentFormDesc = currentVerbData.desc;
                }
                // quick 
                if ( currentVerbData == null )
                {
                    result.bResult = false;
                    result.strResultMsg = "Error: internal error, no verb data generated.";
                    return result;
                }

                string strFmtType = question.eFormatType.ToString();

                string strVerbClasses = "[";
                for ( int i = 0; i < verb.class_type.Length; ++i )
                {
                    strVerbClasses += verb.class_type[i];
                    strVerbClasses += i < verb.class_type.Length - 1 ? "," : "";
                }
                strVerbClasses += "]";

                // 3) build question str
                string strQuestion = string.Format( "Q{0}) Verb: {1} {2} \n\"{3}\"\n{4}, {5}",
                    iLoopIter,
                    verb.name,
                    currentFormName,
                    currentFormDesc,
                    strFmtType.Replace( "_", "-" ) ,
                    strVerbClasses);

                // 4) get all possible answers
                {
                    answers[0] = currentVerbData.kanji;
                    answers[1] = currentVerbData.kana;
                    int iOffset = 1;
                    for ( int en = 0; en < currentVerbData.en.Length; ++en )
                    {
                        if ( !string.IsNullOrEmpty( currentVerbData.en[en] ) )
                        {
                            answers[++iOffset] = currentVerbData.en[en];
                        }
                    }
                }

                // 5) fetch answer
                Console.WriteLine( strQuestion );
                String strAnswer = String.Empty;
                while ( string.IsNullOrEmpty( strAnswer ) )
                {
                    strAnswer = Console.ReadLine();
                }
                
                // remove spaces from answer
                strAnswer = strAnswer.Replace( " ", "" );
               
                // check for quit
                if ( strAnswer == strQuit1 || strAnswer == strQuit2 )
                {
                    result.iNumQuestionsIncorrect = s_reviseList.Count;
                    result.iNumQuestionsCorrect = iLoopIter - s_reviseList.Count;
                    result.bResult = true;
                    result.strResultMsg = "User quit, game completed.";
                    // done
                    break;
                }

                // 6) check answer
                {
                    bool bFailed = true;
                    for ( int i = 0; i < iNUM_POSSIBLE_ANSWERS; ++i )
                    {
                        string strippedPossibleAnswer = answers[i].Replace( " ", "" );
                        if ( !string.IsNullOrEmpty( strippedPossibleAnswer ) && (strippedPossibleAnswer == strAnswer) )
                        {
                            Console.WriteLine( "Correct! {0}", answers[0] );
                            bFailed = false;
                            break;
                        }
                    }
                    if ( bFailed )
                    {
                        Console.WriteLine( "Answer is incorrect the answer is: {0} {1}", answers[2], answers[0] );
                        s_reviseList.Add( new Question( question ) );
                    }
                }

                Console.WriteLine( "" );
                
                // clear answers
                for ( int i = 0; i < iNUM_POSSIBLE_ANSWERS; ++i )
                {
                    answers[i] = string.Empty;
                }
                ++iLoopIter;
            }
            Console.WriteLine( "--------------------------------------------------------------------\n" );

            // save out all the ones you answered wrong
            if ( s_reviseList.Count > 0 )
            {
                Console.WriteLine( "Save and overwrite revision list [Y][Enter] = yes?" );
                Console.WriteLine( "--------------------------------------------------------------------" );
                ConsoleKeyInfo info = Console.ReadKey();

                if ( info.Key == ConsoleKey.Y || info.Key == ConsoleKey.Enter )
                {
                    string strFileName = "revise_list.txt";
                    Task task = SaveReviseListAsync( xVerbs, strFileName );
                    while ( !task.IsCompleted )
                    {
                    }
                    Console.WriteLine( "\n{0} \"{1}\" list was {2}.",
                        task.IsFaulted ? "Error" : "Success",
                        strFileName,
                        task.IsFaulted ? "not saved" : "saved" );
                }
            }

            return result;
        }
        //=================================================================================================

        public static async Task SaveReviseListAsync( Verb_Arr xVerbs, string strFileName )
        {
            string[] lines = new string[s_reviseList.Count];
            for( int i = 0; i < s_reviseList.Count; ++i )
            {
                Question q = s_reviseList[i];
                string strForm = string.Empty;
                string strFormEng = string.Empty;
                if ( q.eFormatType == E_VerbFormat.te_form )
                {
                    strForm = xVerbs.verbs[q.iVerbIndex].te_form.kanji;
                }
                else
                {
                    switch ( q.eFormatType )
                    {
                        case E_VerbFormat.casual_positive: 
                            strForm = xVerbs.verbs[q.iVerbIndex].forms[q.iVerbFormIndex].casual.positive.kanji;
                            strFormEng = xVerbs.verbs[q.iVerbIndex].forms[q.iVerbFormIndex].casual.positive.en[0];
                            break;
                        case E_VerbFormat.casual_negative: 
                            strForm = xVerbs.verbs[q.iVerbIndex].forms[q.iVerbFormIndex].casual.negative.kanji;
                            strFormEng = xVerbs.verbs[q.iVerbIndex].forms[q.iVerbFormIndex].casual.negative.en[0];
                            break;
                        case E_VerbFormat.formal_positive:
                            strForm = xVerbs.verbs[q.iVerbIndex].forms[q.iVerbFormIndex].formal.positive.kanji;
                            strFormEng = xVerbs.verbs[q.iVerbIndex].forms[q.iVerbFormIndex].formal.positive.en[0];
                            break;
                        case E_VerbFormat.formal_negative: 
                            strForm = xVerbs.verbs[q.iVerbIndex].forms[q.iVerbFormIndex].formal.negative.kanji;
                            strFormEng = xVerbs.verbs[q.iVerbIndex].forms[q.iVerbFormIndex].formal.negative.en[0];
                            break;
                        default:
                            break;
                    }
                }

                string strLine = string.Format( "{0},{1},{2} : {3},{4},{5}",
                    q.iVerbIndex, q.iVerbFormIndex, (int)q.eFormatType,
                    xVerbs.verbs[q.iVerbIndex].name,
                    strFormEng,
                    strForm );

                lines[i] = strLine;
            }

            await File.WriteAllLinesAsync( strFileName, lines );
        }

        public static async Task<string[]> LoadReviseListAsync( string strFileName )
        {
            Task<string[]> lines = File.ReadAllLinesAsync( strFileName );
            await lines;
            return lines.GetAwaiter().GetResult();
        }

        public static void Main( string[] args )
        {
            CmdArgs cmdArgs = new CmdArgs();

            Console.WriteLine( "--------------------------------------------------------------------" );
            Console.WriteLine( "[Verb Conjugation] pass in -help for available args" );
            Console.WriteLine( "--------------------------------------------------------------------\n" );

            Console.WriteLine( "Please use \"Lucida Console\" instead of the default font(MenuButton>Default>Font)" );
            Console.WriteLine( "--------------------------------------------------------------------\n" );

            string strFileToLoad = string.Empty;

            // parse args
            for ( int iArg = 0; iArg < args.Length; ++iArg )
            {
                string cmd = args[iArg].ToLower();
                switch ( cmd )
                {
                    case "-help":
                    {
                        Console.WriteLine( "Verb conjugator help!" );
                        Console.WriteLine( "--------------------------------------------------------------------" );
                        Console.WriteLine( "The following are valid commands:" );
                        Console.WriteLine( "--------------------------------------------------------------------" );

                        // mode
                        string strModes = "[";
                        for ( int m = 0; m < (int)E_GameMode._E_NumGameModes; ++m )
                        {
                            E_GameMode e = (E_GameMode)m;
                            strModes += string.Format( "{0}={1}{2}", m, e.ToString(), m < (int)E_GameMode._E_NumGameModes-1 ? "," : "]" );
                        }
                        Console.WriteLine( "-mode [0-{0}] -- {1}", (int)E_GameMode._E_NumGameModes, strModes );

                        // single format param
                        strModes = "[";
                        for ( int m = 0; m < (int)E_VerbFormat._num_formats; ++m )
                        {
                            E_VerbFormat e = (E_VerbFormat)m;
                            strModes += string.Format( "{0}={1}{2}", m, e.ToString(), m < (int)E_VerbFormat._num_formats-1 ? "," : "]" );
                        }
                        Console.WriteLine( "-single_format [0-{0}] -- {1}", (int)E_VerbFormat._num_formats, strModes );

                        // num questions
                        Console.WriteLine( "-num_questions i = used to set the max amount of questions in Random Mode" );
                        Console.WriteLine( "-repeat_verb i = used in game mode single repeat" );
                        Console.WriteLine( "-file s = pass json file to load" );
                        Console.WriteLine( "--------------------------------------------------------------------" );
                        Console.WriteLine( "--------------------------------------------------------------------" );
                        Console.WriteLine( "\n" );
                        break;
                    }
                    case "-file":
                    {
                        int iNextArg = iArg + 1;
                        if ( iNextArg >= args.Length )
                        {
                            Console.WriteLine( "Error no value for cmd" );
                            break;
                        }
                        strFileToLoad = args[iNextArg];
                        iArg++;
                        break;
                    }
                    case "-num_questions":
                    {
                        int iNextArg = iArg + 1;
                        if ( iNextArg >= args.Length )
                        {
                            Console.WriteLine( "Error no value for cmd" );
                            break;
                        }
                        cmdArgs.iNumQuestions = Convert.ToInt32( args[iNextArg] );
                        iArg++; 
                        break;
                    }
                    case "-repeat_verb":
                    {
                        int iNextArg = iArg + 1;
                        if ( iNextArg >= args.Length )
                        {
                            Console.WriteLine( "Error no value for cmd" );
                            break;
                        }

                        string[] strVerbList = args[iNextArg].Split( "," );
                        if ( strVerbList.Length == 0 )
                        {
                            cmdArgs.iSingleVerbIndexes.Add( 0 );
                        }
                        else
                        {
                            for ( int verb = 0; verb < strVerbList.Length; ++verb )
                            {
                                int iIndex = Convert.ToInt32( strVerbList[verb] );
                                cmdArgs.iSingleVerbIndexes.Add( iIndex );
                            }
                        }

                        iArg++;
                        break;
                    }
                    case "-mode":
                    {
                        int iNextArg = iArg + 1;
                        if ( iNextArg >= args.Length )
                        {
                            Console.WriteLine( "Error no value for cmd" );
                            break;
                        }
                        int iMode = Convert.ToInt32( args[iNextArg] );
                        cmdArgs.eGameMode = (E_GameMode)iMode;
                        iArg++;
                        break;
                    }
                    case "-single_format":
                    {
                        int nextIndex = iArg + 1;
                        if ( nextIndex >= args.Length )
                        {
                            Console.WriteLine( "Error no value for cmd" );
                            break;
                        }
                        int iMode = Convert.ToInt32( args[nextIndex] );
                        cmdArgs.eSingleFormatMode = (E_VerbFormat)iMode;
                        iArg++;
                        break;
                    }
                    default:
                    {
                        Console.WriteLine( "Unkown cmd {0}", cmd );
                        break;
                    }
                }
            }

            string strFileName = string.IsNullOrEmpty(strFileToLoad) ?
                "../../../data/data_verbs_small.json" : strFileToLoad;
            try
            {
                byte[] aJsonSrc = File.ReadAllBytes( strFileName );
                Verb_Arr xVerbs = JsonSerializer.Deserialize<Verb_Arr>( aJsonSrc );

                // process
                if ( xVerbs != null )
                {
                    GameResult xRes = MainLoop( cmdArgs, xVerbs );

                    Console.WriteLine( "Game over: {0}", xRes.bResult ? "Success" : "Failure" );
                    Console.WriteLine( xRes.strResultMsg );
                    if ( xRes.bResult )
                    {
                        Console.WriteLine( "Questions Correct: {0}. Questions Incorrect {1}", xRes.iNumQuestionsCorrect, xRes.iNumQuestionsIncorrect );
                    }

                    // Game over man
                    Console.WriteLine( "--------------------------------------------------------------------" );
                    Console.WriteLine( "Program finished.... please press any key to exit" );
                    Console.WriteLine( "--------------------------------------------------------------------" );
                    Console.ReadKey();
                }
            }
            catch ( Exception ex )
            {
                Console.WriteLine( ex.Message );
            }
        }
    }
}
