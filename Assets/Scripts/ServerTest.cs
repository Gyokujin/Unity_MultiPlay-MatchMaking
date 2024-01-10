using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ServerTest : MonoBehaviour
{
    private bool _isServer = false;
    private string _externalServerIP = "0.0.0.0";
    private ushort _serverPort = 7770;

    void Start()
    {
        var args = System.Environment.GetCommandLineArgs();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-dedicatedServer":
                    _isServer = true;
                    break;
                case "-ip":
                    if (args.Length > i + 1)
                    {
                        _externalServerIP = args[i + 1];
                    }
                    break;
                case "-port":
                    if (args.Length > i + 1)
                    {
                        _serverPort = ushort.Parse(args[i + 1]);
                    }
                    break;
            }
        }
    }
}