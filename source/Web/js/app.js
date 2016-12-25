import getBranchesAsync from './server/get-branches-async';

import state from './state';
import uiAsync from './ui';

function applyUpdateResult(updateResult) {
    const result = {
        success: true,
        decompiled: updateResult.x.decompiled,
        errors: [],
        warnings: []
    };
    for (let diagnostic of updateResult.diagnostics) {
        if (diagnostic.severity === 'error') {
            result.success = false;
            result.errors.push(diagnostic);
        }
        else if (diagnostic.severity === 'warning') {
            result.warnings.push(diagnostic);
        }
    }
    this.result = result;
}

function applyServerError(message) {
    this.result = {
        success: false,
        errors: [{ message: message }],
        warnings: []
    };
}

async function createAppAsync() {
    const app = Object.assign({
        codeMirrorModes: {
            csharp: 'text/x-csharp',
            vbnet:  'text/x-vb',
            il:     ''
        },

        branchGroups: [],
        branch: null,

        loading: false,

        result: {
            success: true,
            decompiled: '',
            errors: [],
            warnings: []
        }
    });
    state.load(app);

    const branchesPromise = (async () => {
        const branches = await getBranchesAsync();
        const groups = {};
        for (let branch of branches) {
            let group = groups[branch.group];
            if (!group) {
                group = { name: branch.group, branches: [] };
                groups[branch.group] = group;
                app.branchGroups.push(group);
            }
            group.branches.push(branch);
        }
        return branches;
    })();

    if (app.options.branchId) {
        const branches = await branchesPromise;
        app.branch = branches.filter(b => b.id === app.options.branchId)[0];
    }

    app.applyUpdateResult = applyUpdateResult.bind(app);
    app.applyServerError = applyServerError.bind(app);
    return app;
}

(async function runAsync() {
    const app = await createAppAsync();
    const ui = await uiAsync(app);

    ui.watch('code', () => state.save(app));

    /*for (let name of ['options', 'branch']) {
        ui.watch(name,  () => app.processChangeAsync(), { deep: true });
    }*/
})();