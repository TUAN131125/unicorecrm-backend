import os, subprocess, time, urllib.request, pathlib, json

def run(h):
    h.guard()
    env={**os.environ,'VITE_RUNTIME_MODE':'connected','VITE_API_BASE_URL':h.baseurl,'VITE_AUTH_ADAPTER':'development','DISABLE_HMR':'true',
         'CURSOR_API':h.baseurl,'CURSOR_WEB':h.frontend_url,'CURSOR_EMAIL':h.identities['A_ADMIN']['email'],'CURSOR_PASSWORD':h.passwords['A_ADMIN'],
         'CURSOR_WORKSPACE_KEY':'acceptance-a-'+h.RUN[:8],'CURSOR_WORKSPACE_ID':h.identities['A_ADMIN']['workspaceId'],'CURSOR_OUTPUT':str(h.OUT),'CURSOR_FE':str(h.FE)}
    log=(h.OUT/'vite.log').open('w',encoding='utf-8')
    server=subprocess.Popen(['node',str(h.FE/'node_modules/vite/bin/vite.js'),'--host','127.0.0.1','--port',str(h.frontend_port),'--strictPort'],cwd=h.FE,env=env,stdout=log,stderr=subprocess.STDOUT)
    owned={'pid':server.pid,'kind':'VITE_CONNECTED','owned':True}; h.manifest['processes'].append(owned)
    try:
        for _ in range(120):
            if server.poll() is not None: raise RuntimeError('Vite stopped')
            try: urllib.request.urlopen(h.frontend_url,timeout=2).close(); break
            except Exception: time.sleep(.25)
        else: raise RuntimeError('Vite readiness timeout')
        runner=subprocess.Popen(['node',str(pathlib.Path(__file__).with_name('browser.mjs'))],cwd=h.FE,env=env,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True)
        tracked={'pid':runner.pid,'kind':'PLAYWRIGHT_RUNNER','owned':True}; h.manifest['processes'].append(tracked)
        try:
            try: stdout,stderr=runner.communicate(timeout=240)
            except subprocess.TimeoutExpired:
                runner.terminate(); stdout,stderr=runner.communicate(timeout=15); raise RuntimeError('Browser time limit')
        finally: tracked['exited']=runner.poll() is not None
        (h.OUT/'browser-execution.log').write_text(h.redact(stdout+stderr),encoding='utf-8')
        def result():
            assert runner.returncode==0, 'Browser proof failed; see browser-execution.log'
            return json.loads((h.OUT/'browser-summary.json').read_text())
        h.check('T14-REAL-BROWSER','A_ADMIN','First/next/query reset/refresh/invalid cursor recovery',result,'SYNTHETIC','NOT_RUN_IN_BROWSER')
    finally:
        server.terminate()
        try: server.wait(timeout=15)
        except subprocess.TimeoutExpired: server.kill(); server.wait(timeout=10)
        owned['exited']=True; log.close(); h.save('manifest.json',h.manifest)
