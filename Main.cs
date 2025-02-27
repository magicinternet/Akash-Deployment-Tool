﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace akash_dep
{
    class MainClass
    {
        public static int kMaxCreateRetry = 3;

        public static int kMaxLeaseRetry = 3;
        public static int kMaxLeaseter = 10000;

        public static int kMaxManifestRetry = 1;
        public static int kMaxManifestIter = 1000;

        // -1 create error, -2 Lease Error, -3 Manifest error, -4 exception
        public static int NewDeployment(ref Wallet wl, long numInstances)
        {
            try
            {
                Instance inst = new Instance(ref wl);
                inst.ClearCaches();

                for (int i = 0; i < kMaxCreateRetry && !inst.Create(numInstances); i++)
                {
                }

                var retry = 0;

                while (!inst.CreateLease() || !inst.SelectLease() || !inst.CheckLease())
                {
                    // try one more time
                    System.Threading.Thread.Sleep(kMaxLeaseter);
                    retry++;

                    if (retry > kMaxLeaseRetry)
                    {
                        inst.Close();
                        return -2;
                    }
                }

                retry = 0;

                while (!inst.SendManifest())
                {
                    System.Threading.Thread.Sleep(kMaxManifestIter); // Must also wait to confirm
                    retry++;

                    if (retry > kMaxManifestRetry)
                    {
                        inst.MarkBad(); // bad manifest submission should be marked
                        inst.Close();
                        return -3;
                    }
                }

                Console.WriteLine("deployment done");
                return 0;
            }
            catch
            {
                Console.WriteLine("deployment exception");
                return -4;
            }
        }

        public static void Main(string[] args)
        {
            int numParams = args.Count();

            String configText = File.ReadAllText("./config.js");
            JToken mainConfig = Converters.STRtoJS(configText);

            Akash.LoadCfg(mainConfig); // Load shared params

            Instance.LoadCfg(mainConfig); // Load shared params

            long DEFAULT_CORES = mainConfig["DEFAULT_CORES"].ToObject<long>();

            Akash.Connect();
            Akash.EvalVars();

            Wallet wl = new Wallet();
            wl.LoadCfg(mainConfig); // Load wallet specific
            wl.ClearCache();
            wl.EvalVars();
            wl.Update();

            Instance.LoadBad(); // Load banlist

            InstanceList lst = new InstanceList(ref wl);
            Console.WriteLine("numParams " + numParams);

            if (numParams == 1)
            {
                lst.Query();
                var vars = args[0];

                if (vars == "cleanup")
                {
                    lst.CleanDeployments();
                }
                else if (vars == "deposits")
                {
                    lst.DoDeposits(5);
                }
                else if (vars == "manifests")
                {
                    lst.UpdateManifests();
                }
                else if (vars == "info")
                {
                    lst.Stats();
                    return;
                }
                else
                {
                    Console.WriteLine("invalid params");
                }
            }

            int CREATE_DEPLOYMENTS = mainConfig["CREATE_DEPLOYMENTS"].ToObject<int>();

            var progress = new ProgressConsole(CREATE_DEPLOYMENTS, "creating deployments");

            int numGood = 0;

            for (int i = 0; i < CREATE_DEPLOYMENTS; i++)
            {
                progress.Increment();

                int depStatus = NewDeployment(ref wl, DEFAULT_CORES);

                if (depStatus == 0)
                {
                    numGood++;
                }
                else if (depStatus == -2) // Lease error, stop
                {
                    Console.WriteLine("no more leases available");
                    break;
                }
            }

            Console.WriteLine("Deployed " + numGood + " from " + CREATE_DEPLOYMENTS);
        }
    }
}
