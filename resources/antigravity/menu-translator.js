// Antigravity Chinese Localization Engine - Menu
(function() {
  const menuTranslationMap = {
    'File': '文件', 'Edit': '编辑', 'View': '视图', 'Window': '窗口', 'Help': '帮助',
    'New Conversation': '新建对话', 'New Window': '新建窗口', 'Close Window': '关闭窗口',
    'Check for Updates': '检查更新', 'Checking for Updates...': '正在检查更新...',
    'Downloading Update...': '正在下载更新...', 'Restart to Update': '重启以应用更新',
    'Undo': '撤销', 'Redo': '重做', 'Cut': '剪切', 'Copy': '复制', 'Paste': '粘贴',
    'Select All': '全选', 'Minimize': '最小化', 'Close': '关闭', 'Quit Antigravity': '退出 Antigravity',
    'About Antigravity': '关于 Antigravity', 'Services': '服务', 'Hide Antigravity': '隐藏 Antigravity',
    'Hide Others': '隐藏其他', 'Show All': '显示全部', 'Force Reload': '强制重新加载',
    'Reload': '重新加载', 'Actual Size': '实际大小', 'Zoom In': '放大', 'Zoom Out': '缩小',
    'Toggle Full Screen': '切换全屏',
    'Split': '分屏', 'Split Right': '向右分屏', 'Split Down': '向下分屏', 'Replace With New': '替换为新建'
  };
  function translateMenu(menuItem) {
    if (menuItem.label && menuTranslationMap[menuItem.label]) {
      menuItem.label = menuTranslationMap[menuItem.label];
    }
    if (menuItem.submenu && menuItem.submenu.items) {
      menuItem.submenu.items.forEach(translateMenu);
    }
    if (menuItem.submenu && Array.isArray(menuItem.submenu)) {
      menuItem.submenu.forEach(translateMenu);
    }
  }

  try {
    const { Menu } = require('electron');
    if (Menu && Menu.buildFromTemplate && !Menu.__isTranslated) {
      const originalBuildFromTemplate = Menu.buildFromTemplate;
      Menu.buildFromTemplate = function(template) {
        if (template && Array.isArray(template)) {
          template.forEach(translateMenu);
        }
        return originalBuildFromTemplate.call(this, template);
      };
      Menu.__isTranslated = true;
    }
  } catch(e) {
    console.error("Menu hooking failed:", e);
  }
})();
