#!/usr/bin/env python3
import sys
raise SystemExit(f"command {sys.argv[1] if len(sys.argv)>1 else 'unknown'} is predeclared but not admitted")
